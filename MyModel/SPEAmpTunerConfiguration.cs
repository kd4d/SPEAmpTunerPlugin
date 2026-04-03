#nullable enable

using PgTg.Common;
using PgTg.Plugins.Core;
using SPEAmpTunerPlugin.MyModel.Internal;

namespace SPEAmpTunerPlugin.MyModel
{
    /// <summary>Configuration for SPE Expert combined amplifier + ATU plugin (serial only).</summary>
    public class SPEAmpTunerConfiguration : IAmplifierTunerConfiguration
    {
        public string PluginId { get; set; } = SPEAmpTunerPlugin.PluginId;
        public bool Enabled { get; set; } = false;
        public PluginConnectionType ConnectionType { get; set; } = PluginConnectionType.Serial;
        public string IpAddress { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 5002;
        public string SerialPort { get; set; } = "COM1";
        public int BaudRate { get; set; } = 9600;
        public int ReconnectDelayMs { get; set; } = 5000;
        public bool TcpSupported { get; set; } = false;
        public bool SerialSupported { get; set; } = true;
        public bool WolSupported { get; set; } = false;
        public bool SkipDeviceWakeup { get; set; } = false;

        /// <summary>
        /// When false, Device Control does not auto-gray LEDs on disconnect; Power LED still reflects
        /// <see cref="StatusTracker.GetDeviceData"/> <c>ON</c> from serial connection state.
        /// </summary>
        public bool DisableControlsOnDisconnect { get; set; } = true;

        public int PollingIntervalRxMs { get; set; } = Constants.PollingRxMs;
        public int PollingIntervalTxMs { get; set; } = Constants.PollingTxMs;
        public int PttWatchdogIntervalMs { get; set; } = Constants.PttWatchdogMs;

        public int TuneTimeoutMs { get; set; } = 30000;
    }
}
