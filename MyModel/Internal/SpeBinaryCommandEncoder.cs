#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
namespace SPEAmpTunerPlugin.MyModel.Internal
{
    /// <summary>Maps fictitious <c>$…;</c> strings (Constants / device control) to SPE 6-byte host frames.</summary>
    internal static class SpeBinaryCommandEncoder
    {
        private static readonly Regex SegmentPattern = new(@"\$[^$;]*;", RegexOptions.Compiled);

        /// <summary>All segments in order; FRQ/no-op segments are skipped.</summary>
        public static IReadOnlyList<byte[]> EncodeAll(string? data)
        {
            var list = new List<byte[]>();
            if (string.IsNullOrWhiteSpace(data))
                return list;

            foreach (Match m in SegmentPattern.Matches(data))
            {
                byte[]? frame = EncodeOneSegment(m.Value);
                if (frame != null && frame.Length > 0)
                    list.Add(frame);
            }

            return list;
        }

        private static byte[]? EncodeOneSegment(string segment)
        {
            string s = segment.Trim();
            if (!s.StartsWith("$", StringComparison.Ordinal)) return null;
            if (!s.EndsWith(";", StringComparison.Ordinal)) s += ";";

            string inner = s.Substring(1, s.Length - 2).Trim();
            int sp = inner.IndexOf(' ', StringComparison.Ordinal);
            string key = sp >= 0 ? inner.Substring(0, sp).Trim().ToUpperInvariant() : inner.ToUpperInvariant();
            string arg = sp >= 0 ? inner.Substring(sp + 1).Trim() : string.Empty;

            if (key.StartsWith("FRQ", StringComparison.Ordinal))
                return null;

            // Band index is sent via SpeProtocol.CmdBandInc/Dec from CommandQueue, not a single frame.
            if (key == "BND" && !string.IsNullOrEmpty(arg))
                return null;

            // Software PTT — not part of SPE keyboard protocol; RF/hardware PTT only.
            if (key is "TX15" or "RX")
                return null;

            if (key == "ANT" && int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ant) && ant is >= 1 and <= 4)
                return SpeProtocol.CmdAntennaToggle;

            if (key == "INP" && int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int inp) && inp is >= 1 and <= 2)
                return SpeProtocol.CmdInputToggle;

            return key switch
            {
                "WKP" or "IDN" or "PWR" or "TMP" or "OPR" or "BND" or "VLT" or "ANT" or "BYP" or "TPL" or "SWR" or "FPW" or "IND" or "CAP" or "FLT" or "IN" or "VER" or "SER" or "FLC"
                    => SpeProtocol.StatusPoll,

                "OPR0" or "OS0" or "OPR1" or "OS1" => SpeProtocol.CmdOperateToggle,

                "SDN" => SpeProtocol.CmdSwitchOff,

                "TUN" or "TUS" or "BYPB" or "BYPN" => SpeProtocol.CmdTune,

                "SWT" => SpeProtocol.CmdOperateToggle,

                "PLV" => SpeProtocol.CmdGainToggle,

                _ => null
            };
        }
    }
}
