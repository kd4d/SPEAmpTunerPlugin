#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using PgTg.Common;
using PgTg.Plugins.Core;

namespace SPEAmpTunerPlugin.MyModel.Internal
{
    /// <summary>Serial port I/O with SPE binary framing and fictitious string translation.</summary>
    internal class SerialConnection : ISpeAmpTunerConnection
    {
        private const string ModuleName = "SerialConnection";

        private readonly CancellationToken _cancellationToken;
        private readonly object _lock = new();
        private readonly List<byte> _rxBuffer = new();

        private SerialPort? _serialPort;
        private string _portName = string.Empty;
        private int _baudRate = 38400;
        private bool _isRunning;
        private bool _disposed;
        private bool _sendErrorLogged;

        public event Action<string>? DataReceived;
        public event Action<PluginConnectionState>? ConnectionStateChanged;

        public PluginConnectionState ConnectionState { get; private set; } = PluginConnectionState.Disconnected;

        public bool IsConnected
        {
            get
            {
                lock (_lock)
                {
                    return _serialPort?.IsOpen == true;
                }
            }
        }

        public SerialConnection(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
        }

        public void Configure(string portName, int baudRate = 38400)
        {
            _portName = portName;
            _baudRate = baudRate;
        }

        public Task StartAsync()
        {
            if (_isRunning) return Task.CompletedTask;

            _isRunning = true;
            _ = ConnectAndListenAsync();
            return Task.CompletedTask;
        }

        public void Stop()
        {
            _isRunning = false;
            Disconnect();
        }

        public bool Send(string data)
        {
            if (!IsConnected || _serialPort == null)
                return false;

            byte[]? frame = SpeCommandTranslator.EncodeFictitiousToFrame(data);
            if (frame == null || frame.Length == 0)
                return true;

            try
            {
                _serialPort.Write(frame, 0, frame.Length);
                _sendErrorLogged = false;
                return true;
            }
            catch (InvalidOperationException ex)
            {
                if (!_sendErrorLogged)
                {
                    Logger.LogError(ModuleName, $"Send Error (port not open): {ex.Message}");
                    _sendErrorLogged = true;
                }
                return false;
            }
            catch (TimeoutException ex)
            {
                if (!_sendErrorLogged)
                {
                    Logger.LogError(ModuleName, $"Send Timeout: {ex.Message}");
                    _sendErrorLogged = true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogVerbose(ModuleName, $"Error sending message to device: {ex.Message}");
                return false;
            }
        }

        private async Task ConnectAndListenAsync()
        {
            while (_isRunning && !_cancellationToken.IsCancellationRequested)
            {
                try
                {
                    SetConnectionState(PluginConnectionState.Connecting);

                    lock (_lock)
                    {
                        _serialPort = new SerialPort(_portName, _baudRate, Parity.None, 8, StopBits.One)
                        {
                            ReadTimeout = 500,
                            WriteTimeout = 500,
                            Handshake = Handshake.None,
                            DtrEnable = true,
                            RtsEnable = true
                        };
                    }

                    Logger.LogInfo(ModuleName, $"Attempting to open serial port {_portName} at {_baudRate} baud");
                    _serialPort.Open();

                    if (_serialPort.IsOpen)
                    {
                        SetConnectionState(PluginConnectionState.Connected);
                        _sendErrorLogged = false;
                        Logger.LogInfo(ModuleName, $"Successfully opened {_portName}");

                        _serialPort.DataReceived += OnSerialDataReceived;

                        while (_isRunning && !_cancellationToken.IsCancellationRequested && _serialPort?.IsOpen == true)
                        {
                            await Task.Delay(100, _cancellationToken);
                        }

                        if (_serialPort != null)
                            _serialPort.DataReceived -= OnSerialDataReceived;
                    }
                    else
                    {
                        Logger.LogError(ModuleName, $"Failed to open {_portName}");
                        SetConnectionState(PluginConnectionState.Disconnected);
                        CleanupConnection();
                    }
                }
                catch (ObjectDisposedException) { break; }
                catch (OperationCanceledException) { break; }
                catch (UnauthorizedAccessException ex)
                {
                    Logger.LogError(ModuleName, $"Port {_portName} access denied: {ex.Message}");
                    SetConnectionState(PluginConnectionState.Reconnecting);
                    CleanupConnection();
                }
                catch (IOException ex)
                {
                    Logger.LogVerbose(ModuleName, $"Unable to open serial port: {ex.Message}");
                    SetConnectionState(PluginConnectionState.Reconnecting);
                    CleanupConnection();
                }
                catch (Exception ex)
                {
                    Logger.LogError(ModuleName, $"Connection error: {ex.Message}");
                    SetConnectionState(PluginConnectionState.Reconnecting);
                    CleanupConnection();
                }

                if (_isRunning && !_cancellationToken.IsCancellationRequested)
                    await Task.Delay(2000, _cancellationToken);
            }
        }

        private void OnSerialDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (_serialPort == null || !_serialPort.IsOpen) return;

            try
            {
                int n = _serialPort.BytesToRead;
                if (n <= 0) return;

                byte[] buf = new byte[n];
                int read = _serialPort.Read(buf, 0, n);
                if (read <= 0) return;

                for (int i = 0; i < read; i++)
                    _rxBuffer.Add(buf[i]);

                while (SpeFrameCodec.TryExtractFrame(_rxBuffer, out byte[]? payload))
                {
                    string fictitious = SpeCommandTranslator.DecodePayloadToFictitious(payload);
                    if (fictitious.Length > 0)
                        DataReceived?.Invoke(fictitious);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ModuleName, $"Error reading serial data: {ex.Message}");
            }
        }

        private void Disconnect()
        {
            SetConnectionState(PluginConnectionState.Disconnected);
            CleanupConnection();
        }

        private void CleanupConnection()
        {
            lock (_lock)
            {
                try
                {
                    if (_serialPort != null)
                    {
                        _serialPort.DataReceived -= OnSerialDataReceived;
                        if (_serialPort.IsOpen)
                            _serialPort.Close();
                        _serialPort.Dispose();
                    }
                }
                catch { }
                _serialPort = null;
            }
            _rxBuffer.Clear();
        }

        private void SetConnectionState(PluginConnectionState state)
        {
            if (ConnectionState != state)
            {
                ConnectionState = state;
                ConnectionStateChanged?.Invoke(state);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _isRunning = false;
            CleanupConnection();

            DataReceived = null;
            ConnectionStateChanged = null;
        }
    }
}
