#nullable enable

namespace SPEAmpTunerPlugin.MyModel.Internal
{
    /// <summary>
    /// Fictitious <c>$ … ;</c> command strings for internal use; translated to SPE binary in <see cref="SpeCommandTranslator"/>.
    /// </summary>
    internal static class Constants
    {
        public const double MeterDisplayMaxPower = 1500;

        #region CAT Command Strings

        public const string WakeUpCmd = "$WKP;";
        public const string ShutdownCmd = "$SDN;";
        public const string IdentifyCmd = "$IDN;";
        public const string IdentifyResponse = "SPE";
        public static readonly bool DeviceInitializationEnabled = true;

        public const string OperateCmd = "$OPR1;";
        public const string StandbyCmd = "$OPR0;";

        public const string PttOnCmd = "$TX15;";
        public const string PttOffCmd = "$RX;";

        public const string ClearFaultCmd = "$FLC;";

        /// <summary>Unused for SPE — frequency comes from CAT; band is derived in the plugin.</summary>
        public const string SetFreqKhzCmdPrefix = "$FRQ";

        public const string BypassCmd = "$BYPB;";
        public const string InlineCmd = "$BYPN;";

        public const string TuneStartCmd = "$TUN;";
        public const string TuneStopCmd = "$TUS;";

        public const string Antenna1Cmd = "$ANT 1;";
        public const string Antenna2Cmd = "$ANT 2;";
        public const string Antenna3Cmd = "$ANT 3;";
        public const string Antenna4Cmd = "$ANT 4;";

        public const string Input1Cmd = "$INP 1;";
        public const string Input2Cmd = "$INP 2;";

        /// <summary>SPE switch / power toggle (same command for on and off in UI).</summary>
        public const string SwitchToggleCmd = "$SWT;";

        /// <summary>Cycle SPE output power level (LOW / MID / HIGH).</summary>
        public const string SpePowerLevelCmd = "$PLV;";

        #endregion

        #region Polling Command Arrays

        public static readonly string[] RxPollCommands =
        {
            "$PWR;",
            "$TMP;",
            "$OPR;",
            "$BND;",
            "$VLT;",
            "$ANT;",
            "$BYP;",
            "$TPL;",
            "$SWR;",
            "$IND;",
            "$CAP;",
            "$FLT;",
            "$IN;",
        };

        public static readonly string[] TxPollCommands =
        {
            "$PWR;",
            "$TMP;",
            "$SWR;",
            "$TPL;",
        };

        #endregion

        #region Timing Constants

        public const int PttWatchdogMs = 10000;
        public const int PollingRxMs = 150;
        public const int PollingTxMs = 15;

        #endregion

        #region CAT Response Keys

        public const string KeyTx = "TX";
        public const string KeyRx = "RX";
        public const string KeyPwr = "PWR";
        public const string KeyOpr = "OPR";
        public const string KeyTmp = "TMP";
        public const string KeyVlt = "VLT";
        public const string KeyBnd = "BND";
        public const string KeyFlt = "FLT";
        public const string KeyVer = "VER";
        public const string KeySer = "SER";
        public const string KeyIdn = "IDN";

        public const string KeyByp = "BYP";
        public const string KeyTpl = "TPL";
        public const string KeySwr = "SWR";
        public const string KeyFpw = "FPW";
        public const string KeyInd = "IND";
        public const string KeyCap = "CAP";
        public const string KeyAnt = "ANT";
        public const string KeyIn = "IN";
        /// <summary>SPE power level L / M / H.</summary>
        public const string KeyPlv = "PLV";

        #endregion

        #region Band Mapping

        public static string LookupBandName(int bandNumber)
        {
            return bandNumber switch
            {
                0 => "160m",
                1 => "80m",
                2 => "60m",
                3 => "40m",
                4 => "30m",
                5 => "20m",
                6 => "17m",
                7 => "15m",
                8 => "12m",
                9 => "10m",
                10 => "6m",
                _ => "Unknown"
            };
        }

        #endregion
    }
}
