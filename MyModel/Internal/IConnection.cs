#nullable enable

using System;
using System.Threading.Tasks;
using PgTg.Plugins.Core;

namespace SPEAmpTunerPlugin.MyModel.Internal
{
    /// <summary>Serial connection to SPE Expert (binary on the wire, fictitious strings at the API boundary).</summary>
    internal interface ISpeAmpTunerConnection : IDisposable
    {
        event Action<string>? DataReceived;
        event Action<PluginConnectionState>? ConnectionStateChanged;

        PluginConnectionState ConnectionState { get; }
        bool IsConnected { get; }

        Task StartAsync();
        void Stop();
        bool Send(string data);
    }
}
