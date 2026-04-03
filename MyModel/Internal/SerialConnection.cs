#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PgTg.Common;
using PgTg.Plugins.Core;

namespace SPEAmpTunerPlugin.MyModel.Internal
{
    /// <summary>
    /// RS-232: sends SPE 6-byte host frames; receives either vendor-framed CSV (0xAA…) or plain CSV lines (emulators).
    /// </summary>
    internal class SerialConnection : ISpeAmpTunerConnection
    {
        private const string ModuleName = "SerialConnection";

        private readonly CancellationToken _cancellationToken;
        private readonly object _lock = new();
        private readonly List<byte> _rxBuffer = new(512);

        private SerialPort? _serialPort;
        private string _portName = string.Empty;
        private int _baudRate = SpeProtocol.DefaultBaudRate;
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

        public void Configure(string portName, int baudRate = 0)
        {
            _portName = portName;
            _baudRate = baudRate > 0 ? baudRate : SpeProtocol.DefaultBaudRate;
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

        public bool Send(byte[] data)
        {
            if (data == null || data.Length == 0) return false;
            if (!IsConnected || _serialPort == null) return false;

            try
            {
                _serialPort.Write(data, 0, data.Length);
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

        public bool Send(string data)
        {
            var frames = SpeBinaryCommandEncoder.EncodeAll(data);
            if (frames.Count > 0)
            {
                foreach (byte[] frame in frames)
                {
                    if (!Send(frame))
                        return false;
                }
                return true;
            }

            if (SpeBinaryCommandEncoder.IsNoOpOrFrqOnly(data))
                return true;

            Logger.LogVerbose(ModuleName, "No SPE binary mapping for device command (nothing sent)");
            return false;
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
                            RtsEnable = true,
                            Encoding = Encoding.UTF8
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

                lock (_rxBuffer)
                {
                    for (int i = 0; i < read; i++)
                        _rxBuffer.Add(buf[i]);

                    DrainReceiveBuffer();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ModuleName, $"Error reading serial data: {ex.Message}");
            }
        }

        private void DrainReceiveBuffer()
        {
            while (true)
            {
                if (TryExtractSpeStatusPacket(_rxBuffer, out string? csvLine))
                {
                    DataReceived?.Invoke(csvLine);
                    continue;
                }

                if (TryExtractUtf8Line(_rxBuffer, out string? textLine))
                {
                    DataReceived?.Invoke(textLine);
                    continue;
                }

                break;
            }

            if (_rxBuffer.Count > 65536)
            {
                Logger.LogWarning(ModuleName, "Receive buffer overflow; clearing.");
                _rxBuffer.Clear();
            }
        }

        /// <summary>SPE response: 0xAA×3, len 0x43 (67), 67-byte ASCII CSV, 2-byte checksum, CRLF.</summary>
        private static bool TryExtractSpeStatusPacket(List<byte> buffer, out string line)
        {
            line = string.Empty;
            const int total = 75;
            if (buffer.Count < total) return false;
            if (buffer[0] != 0xAA || buffer[1] != 0xAA || buffer[2] != 0xAA) return false;
            if (buffer[3] != 0x43) return false;

            var dataSpan = buffer.GetRange(4, 67).ToArray();
            if (!VerifySpeChecksum(dataSpan, buffer[71], buffer[72]))
            {
                buffer.RemoveAt(0);
                return false;
            }

            line = Encoding.ASCII.GetString(dataSpan);
            buffer.RemoveRange(0, total);
            return true;
        }

        private static bool VerifySpeChecksum(byte[] data67, byte chk0, byte chk1)
        {
            int sum = 0;
            for (int i = 0; i < data67.Length; i++)
                sum += data67[i];
            return chk0 == (byte)(sum % 256) && chk1 == (byte)(sum / 256);
        }

        private static bool TryExtractUtf8Line(List<byte> buffer, out string line)
        {
            line = string.Empty;
            // Wait for full SPE-framed CSV if binary response is in progress
            if (buffer.Count > 0 && buffer[0] == 0xAA && buffer.Count < 75)
                return false;

            int nl = -1;
            for (int i = 0; i < buffer.Count; i++)
            {
                if (buffer[i] == (byte)'\n')
                {
                    nl = i;
                    break;
                }
            }

            if (nl < 0) return false;

            int end = nl;
            if (end > 0 && buffer[end - 1] == (byte)'\r')
                end--;

            if (end > 0)
                line = Encoding.UTF8.GetString(buffer.GetRange(0, end).ToArray());

            buffer.RemoveRange(0, nl + 1);
            return true;
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

            lock (_rxBuffer)
            {
                _rxBuffer.Clear();
            }
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
