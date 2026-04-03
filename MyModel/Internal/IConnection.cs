#nullable enable

using System;
using System.Threading.Tasks;
using PgTg.Plugins.Core;

namespace SPEAmpTunerPlugin.MyModel.Internal
{
    /// <summary>Serial connection: SPE 6-byte command frames on TX; CSV status lines (or framed CSV) on RX.</summary>
    internal interface ISpeAmpTunerConnection : IDisposable
    {
        event Action<string>? DataReceived;
        event Action<PluginConnectionState>? ConnectionStateChanged;

        PluginConnectionState ConnectionState { get; }
        bool IsConnected { get; }

        Task StartAsync();
        void Stop();

        /// <summary>Send raw SPE host frame(s) (e.g. <see cref="SpeProtocol.StatusPoll"/>).</summary>
        bool Send(byte[] data);

        /// <summary>Encode fictitious <c>$…;</c> segments to SPE frames and send each.</summary>
        bool Send(string data);
    }
}
