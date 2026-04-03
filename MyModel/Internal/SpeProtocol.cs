#nullable enable

namespace SPEAmpTunerPlugin.MyModel.Internal
{
    /// <summary>
    /// SPE Application Programmer's Guide host→amplifier frames (6-byte patterns; checksum = data byte).
    /// See README and vendor PDF.
    /// </summary>
    internal static class SpeProtocol
    {
        public const int DefaultBaudRate = 9600;

        /// <summary>Request status (0x90) — amplifier replies with CSV status line.</summary>
        public static readonly byte[] StatusPoll = { 0x55, 0x55, 0x55, 0x01, 0x90, 0x90 };

        public static readonly byte[] CmdOperateToggle = { 0x55, 0x55, 0x55, 0x01, 0x0D, 0x0D };
        public static readonly byte[] CmdAntennaToggle = { 0x55, 0x55, 0x55, 0x01, 0x04, 0x04 };
        public static readonly byte[] CmdInputToggle = { 0x55, 0x55, 0x55, 0x01, 0x01, 0x01 };
        public static readonly byte[] CmdBandDec = { 0x55, 0x55, 0x55, 0x01, 0x02, 0x02 };
        public static readonly byte[] CmdBandInc = { 0x55, 0x55, 0x55, 0x01, 0x03, 0x03 };
        public static readonly byte[] CmdTune = { 0x55, 0x55, 0x55, 0x01, 0x09, 0x09 };
        public static readonly byte[] CmdGainToggle = { 0x55, 0x55, 0x55, 0x01, 0x0B, 0x0B };
        public static readonly byte[] CmdSwitchOff = { 0x55, 0x55, 0x55, 0x01, 0x0A, 0x0A };
    }
}
