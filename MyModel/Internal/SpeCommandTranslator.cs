#nullable enable

using System;
using System.Globalization;
using System.Text;
using PgTg.Common;

namespace SPEAmpTunerPlugin.MyModel.Internal
{
    /// <summary>
    /// Maps fictitious <c>$KEY …;</c> strings used internally to SPE binary frames, and decodes
    /// status frames back to fictitious responses for <see cref="ResponseParser"/>.
    /// </summary>
    internal static class SpeCommandTranslator
    {
        private const string ModuleName = "SpeCommandTranslator";

        /// <summary>Request status / poll (maps most meter polls).</summary>
        private const byte CmdRequestStatus = 0x01;

        private const byte CmdSetAntenna = 0x02;
        private const byte CmdSetInput = 0x03;
        private const byte CmdSetBand = 0x04;
        private const byte CmdPttOn = 0x05;
        private const byte CmdPttOff = 0x06;
        private const byte CmdStandby = 0x07;
        private const byte CmdOperate = 0x08;
        private const byte CmdClearFault = 0x09;
        private const byte CmdTuneStart = 0x0A;
        private const byte CmdTuneStop = 0x0B;
        private const byte CmdBypass = 0x0C;
        private const byte CmdInline = 0x0D;
        private const byte CmdSwitchToggle = 0x0E;
        private const byte CmdPowerLevelCycle = 0x0F;
        private const byte CmdIdentifyWake = 0x10;
        private const byte CmdShutdown = 0x11;

        /// <summary>Full status response from amplifier (emulator + decoder must match).</summary>
        internal const byte RspFullStatus = 0x80;

        /// <summary>
        /// Builds a binary frame for the given fictitious command, or <c>null</c> if nothing should be sent (e.g. frequency-only placeholder).
        /// </summary>
        public static byte[]? EncodeFictitiousToFrame(string data)
        {
            if (string.IsNullOrWhiteSpace(data)) return null;

            string s = data.Trim();
            if (!s.StartsWith("$", StringComparison.Ordinal))
                s = "$" + s;
            if (!s.EndsWith(";", StringComparison.Ordinal))
                s += ";";

            string inner = s.Substring(1, s.Length - 2).Trim();
            int sp = inner.IndexOf(' ');
            string key = sp >= 0 ? inner.Substring(0, sp).Trim().ToUpperInvariant() : inner.ToUpperInvariant();
            string arg = sp >= 0 ? inner.Substring(sp + 1).Trim() : string.Empty;

            // No-op: CAT frequency — band is driven via <see cref="SpeBandLookup"/> + $BND only.
            if (key.StartsWith("FRQ", StringComparison.Ordinal))
                return null;

            byte[]? payload = EncodeKeyToPayload(key, arg);

            if (payload == null || payload.Length == 0)
            {
                Logger.LogVerbose(ModuleName, $"No SPE mapping for command: {s}");
                return null;
            }

            return SpeFrameCodec.BuildFrame(payload);
        }

        private static byte[]? EncodeKeyToPayload(string key, string arg)
        {
            if (key == "ANT" && int.TryParse(arg, out int ant) && ant is >= 1 and <= 4)
                return new[] { CmdSetAntenna, (byte)ant };

            if (key == "BND" && int.TryParse(arg, out int bnd) && bnd is >= 0 and <= 10)
                return new[] { CmdSetBand, (byte)bnd };

            if (key == "INP" && int.TryParse(arg, out int inp) && inp is >= 1 and <= 2)
                return new[] { CmdSetInput, (byte)inp };

            return key switch
            {
                "WKP" or "IDN" or "PWR" or "TMP" or "OPR" or "BND" or "VLT" or "ANT" or "BYP" or "TPL" or "SWR" or "FPW" or "IND" or "CAP" or "FLT" or "IN" or "VER" or "SER"
                    => new[] { CmdRequestStatus },

                "TX15" => new[] { CmdPttOn },
                "RX" => new[] { CmdPttOff },

                "OPR0" or "OS0" => new[] { CmdStandby },
                "OPR1" or "OS1" => new[] { CmdOperate },

                "FLC" => new[] { CmdClearFault },

                "TUN" => new[] { CmdTuneStart },
                "TUS" => new[] { CmdTuneStop },

                "BYPB" => new[] { CmdBypass },
                "BYPN" => new[] { CmdInline },

                "SDN" => new[] { CmdShutdown },

                "SWT" or "ON0" or "ON1" => new[] { CmdSwitchToggle },
                "PLV" => new[] { CmdPowerLevelCycle },
                "AI0" => new[] { CmdBypass },
                "AI1" => new[] { CmdInline },

                _ => Array.Empty<byte>()
            };
        }

        /// <summary>
        /// Decodes one SPE payload (bytes after length) into fictitious <c>$…;</c> text for the parser.
        /// </summary>
        public static string DecodePayloadToFictitious(ReadOnlySpan<byte> payload)
        {
            if (payload.Length == 0) return string.Empty;

            if (payload[0] == RspFullStatus)
                return DecodeFullStatus(payload.Slice(1));

            return string.Empty;
        }

        private static string DecodeFullStatus(ReadOnlySpan<byte> p)
        {
            // Layout (must match emulator and any real SPE status decode you add from the PDF):
            // 0-1 power watts BE, 2-3 swr_atu*10 BE, 4-5 swr_ant*100 BE, 6 temp, 7 flags,
            // 8 band, 9-10 volt 0.1V, 11-12 curr mA, 13 ant, 14 in, 15 fault, 16 pwrLevel 0=L 1=M 2=H
            if (p.Length < 17) return string.Empty;

            int pwr = ReadUInt16Be(p, 0);
            int swrAtu10 = ReadUInt16Be(p, 2);
            int swrAnt100 = ReadUInt16Be(p, 4);
            int temp = p[6];
            byte flags = p[7];
            int band = p[8];
            int volt10 = ReadUInt16Be(p, 9);
            int curMa = ReadUInt16Be(p, 11);
            int ant = p[13];
            int inp = p[14];
            int fault = p[15];
            int lvl = p[16];

            bool ptt = (flags & 1) != 0;
            bool operate = (flags & 2) != 0;
            bool bypass = (flags & 4) != 0;
            bool tuning = (flags & 8) != 0;

            double swrAtu = swrAtu10 / 10.0;
            if (swrAtu < 1.0) swrAtu = 1.0;
            double swrAnt = swrAnt100 / 100.0;
            if (swrAnt < 1.0) swrAnt = 1.0;

            double v = volt10 / 10.0;
            double cur = curMa / 1000.0;

            string pl = lvl switch { 0 => "L", 1 => "M", 2 => "H", _ => "M" };
            string byp = bypass ? "B" : "N";
            string opr = operate ? "1" : "0";
            string tpl = tuning ? "1" : "0";

            var sb = new StringBuilder(256);
            if (ptt)
                sb.Append("$TX;");
            else
                sb.Append("$RX;");

            sb.Append(CultureInfo.InvariantCulture, $"$PWR {pwr} {(int)(swrAtu * 10)};");
            sb.Append($"$TMP {temp};");
            sb.Append($"$OPR {opr};");
            sb.Append($"$BND {band};");
            sb.Append(CultureInfo.InvariantCulture, $"$VLT {v:0.###} {cur:0.###};");
            sb.Append($"$ANT {ant};");
            sb.Append($"$BYP {byp};");
            sb.Append($"$TPL {tpl};");
            sb.Append(CultureInfo.InvariantCulture, $"$SWR {swrAnt:0.##};");
            sb.Append($"$IND 0;");
            sb.Append($"$CAP 0;");
            sb.Append($"$FLT {fault};");
            sb.Append($"$IN {inp};");
            sb.Append($"$PLV {pl};");
            sb.Append("$IDN SPE;");
            sb.Append("$VER 1.00;");

            return sb.ToString();
        }

        private static int ReadUInt16Be(ReadOnlySpan<byte> b, int o) => (b[o] << 8) | b[o + 1];

        /// <summary>Builds a full status response frame (for tests / emulator parity).</summary>
        public static byte[] BuildFullStatusResponse(
            int powerWatts,
            int swrAtuTenths,
            int swrAntHundredths,
            int tempC,
            bool ptt,
            bool operate,
            bool bypass,
            bool tuning,
            int band0To10,
            double voltage,
            double currentAmps,
            int antenna1To4,
            int input1To2,
            int faultCode,
            int powerLevel012)
        {
            var body = new byte[1 + 17];
            body[0] = RspFullStatus;
            WriteUInt16Be(body.AsSpan(1), 0, (ushort)Math.Clamp(powerWatts, 0, 65535));
            WriteUInt16Be(body.AsSpan(1), 2, (ushort)Math.Clamp(swrAtuTenths, 0, 65535));
            WriteUInt16Be(body.AsSpan(1), 4, (ushort)Math.Clamp(swrAntHundredths, 0, 65535));
            body[1 + 6] = (byte)Math.Clamp(tempC, 0, 255);
            byte f = 0;
            if (ptt) f |= 1;
            if (operate) f |= 2;
            if (bypass) f |= 4;
            if (tuning) f |= 8;
            body[1 + 7] = f;
            body[1 + 8] = (byte)Math.Clamp(band0To10, 0, 10);
            int v10 = (int)Math.Round(voltage * 10.0);
            WriteUInt16Be(body.AsSpan(1), 9, (ushort)Math.Clamp(v10, 0, 65535));
            int ma = (int)Math.Round(currentAmps * 1000.0);
            WriteUInt16Be(body.AsSpan(1), 11, (ushort)Math.Clamp(ma, 0, 65535));
            body[1 + 13] = (byte)Math.Clamp(antenna1To4, 1, 4);
            body[1 + 14] = (byte)Math.Clamp(input1To2, 1, 2);
            body[1 + 15] = (byte)Math.Clamp(faultCode, 0, 255);
            body[1 + 16] = (byte)Math.Clamp(powerLevel012, 0, 2);

            return SpeFrameCodec.BuildFrame(body);
        }

        private static void WriteUInt16Be(Span<byte> body, int offset, ushort value)
        {
            body[offset] = (byte)(value >> 8);
            body[offset + 1] = (byte)value;
        }
    }
}
