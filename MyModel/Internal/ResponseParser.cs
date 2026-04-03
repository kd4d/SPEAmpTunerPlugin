#nullable enable

using System;
using System.Globalization;
using PgTg.AMP;
using PgTg.Common;
using PgTg.Plugins.Core;

namespace SPEAmpTunerPlugin.MyModel.Internal
{
    /// <summary>Parses fictitious <c>$KEY …;</c> lines produced by <see cref="SpeCommandTranslator"/>.</summary>
    internal class ResponseParser
    {
        private const string ModuleName = "ResponseParser";

        public class StatusUpdate
        {
            public AmpOperateState? AmpState { get; set; }
            public bool? IsPtt { get; set; }
            public double? ForwardPower { get; set; }
            public double? SWR { get; set; }
            public double? ReturnLoss { get; set; }
            public int? Temperature { get; set; }
            public double? Voltage { get; set; }
            public double? Current { get; set; }
            public int? BandNumber { get; set; }
            public string? BandName { get; set; }
            public int? FaultCode { get; set; }
            public string? SerialNumber { get; set; }
            public double? FirmwareVersion { get; set; }

            public TunerOperateState? TunerState { get; set; }
            public TunerTuningState? TuningState { get; set; }
            public int? InductorValue { get; set; }
            public int? CapacitorValue { get; set; }
            public int? Antenna { get; set; }
            public int? Input { get; set; }
            public string? PowerLevel { get; set; }
            public double? TunerSWR { get; set; }
            public int? VFWD { get; set; }

            public bool AmpStateChanged { get; set; }
            public bool PttStateChanged { get; set; }
            public bool PttReady { get; set; }
            public bool IsVitaDataPopulated { get; set; }

            public bool TunerStateChanged { get; set; }
            public bool TuningStateChanged { get; set; }
            public bool TunerRelaysChanged { get; set; }
        }

        public StatusUpdate Parse(string response, StatusTracker tracker)
        {
            var update = new StatusUpdate();

            if (!response.EndsWith(";"))
                response += ";";

            string[] parts = response.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string part in parts)
            {
                if (part.Length < 2) continue;

                string trimmed = part.Trim();
                if (!trimmed.StartsWith("$")) continue;

                string content = trimmed.Substring(1);
                int spaceIndex = content.IndexOf(' ');
                string key;
                string value;

                if (spaceIndex >= 0)
                {
                    key = content.Substring(0, spaceIndex).Trim();
                    value = content.Substring(spaceIndex + 1).Trim();
                }
                else
                {
                    key = content.Trim();
                    value = string.Empty;
                }

                if (key.Length == 0) continue;

                ProcessParsedResponse(key, value, update, tracker);
            }

            return update;
        }

        private void ProcessParsedResponse(string key, string value, StatusUpdate update, StatusTracker tracker)
        {
            switch (key)
            {
                case Constants.KeyTx:
                    update.IsPtt = true;
                    update.PttReady = !tracker.IsPtt;
                    if (!tracker.IsPtt)
                        update.PttStateChanged = true;
                    break;

                case Constants.KeyRx:
                    update.IsPtt = false;
                    if (tracker.IsPtt)
                        update.PttStateChanged = true;
                    break;

                case Constants.KeyPwr:
                    ParsePowerSwr(value, update);
                    break;

                case Constants.KeyOpr:
                    var osState = value == "1" ? AmpOperateState.Operate : AmpOperateState.Standby;
                    if (tracker.AmpState != osState)
                    {
                        update.AmpState = osState;
                        update.AmpStateChanged = true;
                    }
                    break;

                case Constants.KeyTmp:
                    if (int.TryParse(value, out int temp))
                    {
                        update.Temperature = temp;
                        update.IsVitaDataPopulated = true;
                    }
                    break;

                case Constants.KeyVlt:
                    ParseVoltageCurrent(value, update);
                    break;

                case Constants.KeyBnd:
                    if (int.TryParse(value, out int band))
                    {
                        update.BandNumber = band;
                        update.BandName = Constants.LookupBandName(band);
                    }
                    break;

                case Constants.KeyFlt:
                    if (int.TryParse(value, out int fault))
                        update.FaultCode = fault;
                    break;

                case Constants.KeyVer:
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double ver))
                        update.FirmwareVersion = ver;
                    break;

                case Constants.KeySer:
                    update.SerialNumber = value;
                    break;

                case Constants.KeyIdn:
                    break;

                case Constants.KeyByp:
                    var tunerState = value == "B" ? TunerOperateState.Bypass : TunerOperateState.Inline;
                    if (tracker.TunerState != tunerState)
                    {
                        update.TunerState = tunerState;
                        update.TunerStateChanged = true;
                    }
                    break;

                case Constants.KeyTpl:
                    var tuningState = value == "1" ? TunerTuningState.TuningInProgress : TunerTuningState.NotTuning;
                    if (tracker.TuningState != tuningState)
                    {
                        update.TuningState = tuningState;
                        update.TuningStateChanged = true;
                    }
                    break;

                case Constants.KeySwr:
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double tunerSwr))
                    {
                        if (tunerSwr < 1.0) tunerSwr = 1.0;
                        update.TunerSWR = tunerSwr;
                        update.IsVitaDataPopulated = true;
                    }
                    break;

                case Constants.KeyFpw:
                    if (int.TryParse(value, out int vfwd))
                    {
                        update.VFWD = vfwd;
                        update.IsVitaDataPopulated = true;
                    }
                    break;

                case Constants.KeyInd:
                    update.InductorValue = 0;
                    update.TunerRelaysChanged = false;
                    break;

                case Constants.KeyCap:
                    update.CapacitorValue = 0;
                    update.TunerRelaysChanged = false;
                    break;

                case Constants.KeyAnt:
                    if (int.TryParse(value, out int ant))
                        update.Antenna = ant;
                    break;

                case Constants.KeyIn:
                    if (int.TryParse(value, out int inp))
                        update.Input = inp;
                    break;

                case Constants.KeyPlv:
                    if (value is "L" or "M" or "H")
                        update.PowerLevel = value;
                    break;

                default:
                    Logger.LogVerbose(ModuleName, $"Unknown response key: {key}");
                    break;
            }
        }

        private void ParsePowerSwr(string value, StatusUpdate update)
        {
            var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                if (int.TryParse(parts[0], out int power))
                {
                    update.ForwardPower = power;
                    update.VFWD = power;
                    update.IsVitaDataPopulated = true;
                }

                if (int.TryParse(parts[1], out int swrRaw))
                {
                    double swr = swrRaw / 10.0;
                    if (swr < 1.0) swr = 1.0;
                    update.SWR = swr;
                    update.ReturnLoss = SwrToReturnLoss(swr);
                    update.IsVitaDataPopulated = true;
                }
            }
            else if (parts.Length == 1)
            {
                if (int.TryParse(parts[0], out int power))
                {
                    update.ForwardPower = power;
                    update.VFWD = power;
                    update.IsVitaDataPopulated = true;
                }
            }
        }

        private void ParseVoltageCurrent(string value, StatusUpdate update)
        {
            var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double voltage))
                    update.Voltage = voltage;

                if (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double current))
                    update.Current = current;
            }
        }

        private static double SwrToReturnLoss(double swr)
        {
            if (swr <= 1.0) return 99.0;
            double rho = (swr - 1.0) / (swr + 1.0);
            if (rho <= 0) return 99.0;
            return Math.Round(-20.0 * Math.Log10(rho), 1);
        }
    }
}
