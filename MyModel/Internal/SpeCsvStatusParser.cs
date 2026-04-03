#nullable enable

using System;
using System.Globalization;
using PgTg.AMP;

namespace SPEAmpTunerPlugin.MyModel.Internal
{
    /// <summary>Parses comma-separated status lines from SPE status poll responses (Application Programmer's Guide).</summary>
    internal static class SpeCsvStatusParser
    {
        public const int MinimumFieldCount = 20;

        public static bool TryParse(string line, out SpeCsvParseResult result)
        {
            result = new SpeCsvParseResult();
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var fields = line.Split(',');
            if (fields.Length < MinimumFieldCount)
                return false;

            static string F(string[] a, int i) => i < a.Length ? a[i].Trim() : "";

            string op = F(fields, 2);
            if (op == "O")
                result.AmpState = AmpOperateState.Operate;
            else if (op == "S")
                result.AmpState = AmpOperateState.Standby;
            else
                result.AmpState = AmpOperateState.Unknown;

            string tx = F(fields, 3);
            result.IsPtt = tx == "T";

            result.BandName = MapBand(F(fields, 6));
            if (int.TryParse(F(fields, 6), NumberStyles.Integer, CultureInfo.InvariantCulture, out int bandNum))
                result.BandNumber = bandNum;

            _ = double.TryParse(F(fields, 10), NumberStyles.Float, CultureInfo.InvariantCulture, out double pOut);
            result.ForwardPower = pOut;

            _ = double.TryParse(F(fields, 11), NumberStyles.Float, CultureInfo.InvariantCulture, out double swr);
            result.Swr = swr > 0 ? swr : 1.0;

            _ = double.TryParse(F(fields, 13), NumberStyles.Float, CultureInfo.InvariantCulture, out double v);
            result.Voltage = v;

            _ = double.TryParse(F(fields, 14), NumberStyles.Float, CultureInfo.InvariantCulture, out double cur);
            result.Current = cur;

            if (int.TryParse(F(fields, 15), NumberStyles.Integer, CultureInfo.InvariantCulture, out int temp))
                result.Temperature = temp;

            result.WarningCode = fields.Length > 18 ? F(fields, 18) : "";
            result.ErrorCode = fields.Length > 19 ? F(fields, 19) : "";

            // Typical emulator layout (leading empty fields): index 7 often holds antenna 1–4; index 4 input 1–2.
            if (fields.Length > 7)
            {
                string a = F(fields, 7);
                if (a.Length == 1 && a[0] >= '1' && a[0] <= '4')
                    result.Antenna = a[0] - '0';
            }
            if (fields.Length > 4)
            {
                string inp = F(fields, 4);
                if (inp == "1" || inp == "2")
                    result.Input = int.Parse(inp, CultureInfo.InvariantCulture);
            }
            foreach (string raw in fields)
            {
                string t = raw.Trim();
                if (t.Length == 1 && (t[0] == 'L' || t[0] == 'M' || t[0] == 'H' || t[0] == 'l' || t[0] == 'm' || t[0] == 'h'))
                {
                    result.PowerLevel = t.ToUpperInvariant();
                    break;
                }
            }

            result.IsValid = true;
            return true;
        }

        private static string MapBand(string code)
        {
            return code switch
            {
                "00" => "160m",
                "01" => "80m",
                "02" => "60m",
                "03" => "40m",
                "04" => "30m",
                "05" => "20m",
                "06" => "17m",
                "07" => "15m",
                "08" => "12m",
                "09" => "10m",
                "10" => "6m",
                "11" => "4m",
                _ => code
            };
        }
    }

    internal sealed class SpeCsvParseResult
    {
        public bool IsValid { get; set; }
        public AmpOperateState AmpState { get; set; } = AmpOperateState.Unknown;
        public bool IsPtt { get; set; }
        public double ForwardPower { get; set; }
        public double Swr { get; set; } = 1.0;
        public double Voltage { get; set; }
        public double Current { get; set; }
        public int Temperature { get; set; }
        public string BandName { get; set; } = "";
        public int BandNumber { get; set; }
        public string WarningCode { get; set; } = "";
        public string ErrorCode { get; set; } = "";
        public int? Antenna { get; set; }
        public int? Input { get; set; }
        public string? PowerLevel { get; set; }
    }
}
