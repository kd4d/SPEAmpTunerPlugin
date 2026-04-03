#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using PgTg.AMP;
using PgTg.Common;
using Timer = System.Timers.Timer;

namespace SPEAmpTunerPlugin.MyModel.Internal
{
    /// <summary>
    /// Command polling and priority queue for SPE Expert (fictitious <c>$…;</c> commands only).
    /// </summary>
    internal class CommandQueue : IDisposable
    {
        private const string ModuleName = "CommandQueue";

        private readonly ISpeAmpTunerConnection _connection;
        private readonly object _priorityLock = new();

        private Timer? _pollTimer;
        private Timer? _pttWatchdogTimer;
        private Timer? _initTimer;
        private CancellationTokenRegistration _timerRegistration;

        private string _priorityCommands = string.Empty;
        private int _rxPollIndex;
        private int _txPollIndex;
        private bool _isPtt;
        private bool _isTuning;
        private bool _waitingForTxAck;
        private bool _pttInProgress;
        private double _fwVersion;
        private bool _disposed;
        private bool _isInitialized;
        private bool _initializationInProgress;
        private bool _initRetryLogged;
        private TaskCompletionSource<bool>? _initCompletionSource;

        private int _pollingRxMs = Constants.PollingRxMs;
        private int _pollingTxMs = Constants.PollingTxMs;
        private int _pttWatchdogMs = Constants.PttWatchdogMs;
        private const int InitRetryIntervalMs = 500;

        public bool IsPtt => _isPtt;
        public bool WaitingForTxAck => _waitingForTxAck;
        public double FirmwareVersion => _fwVersion;
        public bool IsInitialized => _isInitialized;
        public bool SkipDeviceWakeup { get; set; }

        public CommandQueue(ISpeAmpTunerConnection connection, CancellationToken cancellationToken)
        {
            _connection = connection;
            _connection.DataReceived += OnDataReceived;

            _timerRegistration = cancellationToken.Register(() =>
            {
                _initTimer?.Stop();
                _pollTimer?.Stop();
                _pttWatchdogTimer?.Stop();
                _initCompletionSource?.TrySetCanceled();
            });
        }

        private void OnDataReceived(string data)
        {
            if (_initializationInProgress)
                OnInitializationResponse(data);
        }

        public void Configure(int pollingRxMs, int pollingTxMs, int pttWatchdogMs)
        {
            _pollingRxMs = pollingRxMs;
            _pollingTxMs = pollingTxMs;
            _pttWatchdogMs = pttWatchdogMs;
        }

        public async Task StartAsync()
        {
            _pollTimer = new Timer { Interval = _pollingRxMs };
            _pollTimer.Elapsed += OnPollTimerElapsed;

            _pttWatchdogTimer = new Timer { Interval = _pttWatchdogMs };
            _pttWatchdogTimer.Elapsed += OnPttWatchdogTimerElapsed;

            if (Constants.DeviceInitializationEnabled && !SkipDeviceWakeup)
            {
                await InitializeDeviceAsync();
            }
            else
            {
                if (SkipDeviceWakeup)
                    Logger.LogVerbose(ModuleName, "Skipping device initialization (AmpWakeupMode != 1)");
                else
                    Logger.LogVerbose(ModuleName, "Device initialization disabled, starting normal polling immediately");
                _pollTimer?.Start();
            }
        }

        public async Task InitializeDeviceAsync()
        {
            _isInitialized = false;
            _initializationInProgress = true;
            _initCompletionSource = new TaskCompletionSource<bool>();

            string initSequence = Constants.WakeUpCmd + Constants.IdentifyCmd;
            _connection.Send(initSequence);
            Logger.LogVerbose(ModuleName, "Sent device initialization sequence, waiting for response");

            _initTimer = new Timer { Interval = InitRetryIntervalMs };
            _initTimer.Elapsed += OnInitTimerElapsed;
            _initTimer.Start();

            await _initCompletionSource.Task;
        }

        private void OnInitTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            if (!_initializationInProgress || !_connection.IsConnected)
            {
                _initTimer?.Stop();
                return;
            }

            _connection.Send(Constants.WakeUpCmd);
            if (!_initRetryLogged)
            {
                Logger.LogVerbose(ModuleName, "Resending wake-up command for device initialization");
                _initRetryLogged = true;
            }
        }

        /// <summary>Initialization completes when a SPE status decode yields a fictitious response containing <c>;</c>.</summary>
        public bool OnInitializationResponse(string response)
        {
            if (!_initializationInProgress)
                return _isInitialized;

            if (response.Contains(';'))
            {
                if (_initTimer != null)
                {
                    _initTimer.Elapsed -= OnInitTimerElapsed;
                    _initTimer.Stop();
                    _initTimer.Dispose();
                    _initTimer = null;
                }

                _connection.DataReceived -= OnDataReceived;

                _initializationInProgress = false;
                _isInitialized = true;

                Logger.LogVerbose(ModuleName, "Device detected, starting normal polling");

                _pollTimer?.Start();

                _initCompletionSource?.TrySetResult(true);
                return true;
            }

            return false;
        }

        public void Stop()
        {
            _initTimer?.Stop();
            _pollTimer?.Stop();
            _pttWatchdogTimer?.Stop();
        }

        public void SendPriorityCommand(AmpCommand command, AmpOperateState currentState)
        {
            string sendNowCommand;
            switch (command)
            {
                case AmpCommand.RX:
                    _pttWatchdogTimer?.Stop();
                    _pttInProgress = false;
                    _waitingForTxAck = false;

                    sendNowCommand = Constants.PttOffCmd;
                    _connection.Send(sendNowCommand);
                    Logger.LogVerbose(ModuleName, $"Priority RX command sent: {sendNowCommand}");
                    break;

                case AmpCommand.TX:
                case AmpCommand.TXforTuneCarrier:
                    _waitingForTxAck = true;
                    sendNowCommand = Constants.PttOnCmd;

                    if (_pollTimer != null && _pollTimer.Interval != _pollingTxMs)
                    {
                        _pollTimer.Stop();
                        _pollTimer.Interval = _pollingTxMs;
                        _pollTimer.Start();
                    }

                    _connection.Send(sendNowCommand);
                    _pttWatchdogTimer?.Start();
                    _pttInProgress = true;
                    Logger.LogVerbose(ModuleName, $"Priority TX command sent: {sendNowCommand}");
                    break;
            }
        }

        public void SetTunerInline(bool inline)
        {
            lock (_priorityLock)
            {
                _priorityCommands = inline
                    ? Constants.InlineCmd
                    : Constants.TuneStopCmd + Constants.BypassCmd;
            }
            Logger.LogVerbose(ModuleName, $"Queued {(inline ? "INLINE" : "BYPASS")} command");
        }

        public void SetTuneStart(bool start)
        {
            lock (_priorityLock)
            {
                _priorityCommands = start
                    ? Constants.TuneStartCmd
                    : Constants.TuneStopCmd;
            }
            Logger.LogVerbose(ModuleName, $"Queued {(start ? "TUNE START" : "TUNE STOP")} command");

            if (start)
                OnTuningStateChanged(true);
        }

        public void SetOperateMode(bool operate)
        {
            lock (_priorityLock)
            {
                _priorityCommands = operate
                    ? Constants.ClearFaultCmd + Constants.OperateCmd
                    : Constants.StandbyCmd;
            }
            Logger.LogVerbose(ModuleName, $"Queued {(operate ? "OPERATE" : "STANDBY")} command");
        }

        /// <summary>Sends SPE band selection only (0–10). Frequency is not sent to the amplifier.</summary>
        public void SetBandIndex(int bandIndex)
        {
            if (bandIndex < 0 || bandIndex > 10) return;
            _connection.Send($"$BND {bandIndex};");
        }

        public void OnTxRxResponseReceived(bool isTx)
        {
            if (_disposed) return;

            _waitingForTxAck = false;
            _isPtt = isTx;

            if (_pollTimer != null)
            {
                bool needsFastPolling = _isPtt || _isTuning || _waitingForTxAck;
                int targetInterval = needsFastPolling ? _pollingTxMs : _pollingRxMs;
                if (_pollTimer.Interval != targetInterval)
                {
                    _pollTimer.Stop();
                    _pollTimer.Interval = targetInterval;
                    _pollTimer.Start();
                }
            }
        }

        public void OnPttStateChanged(bool isPtt)
        {
            if (_disposed) return;

            _isPtt = isPtt;

            if (_pollTimer != null)
            {
                bool needsFastPolling = _isPtt || _isTuning || _waitingForTxAck;
                int targetInterval = needsFastPolling ? _pollingTxMs : _pollingRxMs;
                if (_pollTimer.Interval != targetInterval)
                {
                    _pollTimer.Stop();
                    _pollTimer.Interval = targetInterval;
                    _pollTimer.Start();
                }
            }
        }

        public void OnTuningStateChanged(bool isTuning)
        {
            if (_disposed) return;

            _isTuning = isTuning;

            if (_pollTimer != null)
            {
                bool needsFastPolling = _isTuning || _isPtt || _waitingForTxAck;
                int targetInterval = needsFastPolling ? _pollingTxMs : _pollingRxMs;
                if (_pollTimer.Interval != targetInterval)
                {
                    _pollTimer.Stop();
                    _pollTimer.Interval = targetInterval;
                    _pollTimer.Start();
                }
            }
        }

        public void SetFirmwareVersion(double version)
        {
            _fwVersion = version;
            Logger.LogVerbose(ModuleName, $"Device FW Version: {_fwVersion}");
        }

        public void ForceReleasesPtt()
        {
            if (_pttInProgress)
            {
                _pttWatchdogTimer?.Stop();
                _pttInProgress = false;
                _waitingForTxAck = false;
                SendPriorityCommand(AmpCommand.RX, AmpOperateState.Unknown);
                Logger.LogVerbose(ModuleName, "Forced device to RX (Safety Measure)");
            }
        }

        private void OnPollTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            if (!_connection.IsConnected) return;

            string cmdsToSend;
            bool needsFastPolling = _isPtt || _isTuning || _waitingForTxAck;

            if (needsFastPolling)
            {
                cmdsToSend = GetNextPollCommand(true);
            }
            else
            {
                cmdsToSend = GetNextPollCommand(false);
                if (_fwVersion == 0.0)
                {
                    cmdsToSend = Constants.IdentifyCmd;
                    Logger.LogVerbose(ModuleName, "Requesting device firmware version");
                }
            }

            SendCommand(cmdsToSend);
        }

        private void OnPttWatchdogTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            if (!_connection.IsConnected)
            {
                _pttWatchdogTimer?.Stop();
                return;
            }

            if (_pttInProgress)
            {
                lock (_priorityLock)
                {
                    _priorityCommands = Constants.PttOnCmd;
                }
            }
        }

        private string GetNextPollCommand(bool isFastPolling)
        {
            if (_waitingForTxAck)
                return string.Empty;

            string command;
            if (isFastPolling)
            {
                command = Constants.TxPollCommands[_txPollIndex];
                _txPollIndex = (_txPollIndex + 1) % Constants.TxPollCommands.Length;
            }
            else
            {
                command = Constants.RxPollCommands[_rxPollIndex];
                _rxPollIndex = (_rxPollIndex + 1) % Constants.RxPollCommands.Length;
            }

            return command;
        }

        private void SendCommand(string message)
        {
            if (string.IsNullOrEmpty(message) && string.IsNullOrEmpty(_priorityCommands))
                return;

            string? priorityToSend = null;
            lock (_priorityLock)
            {
                if (_priorityCommands.Length > 0)
                {
                    priorityToSend = _priorityCommands;
                    _priorityCommands = string.Empty;
                }
            }

            if (priorityToSend != null)
            {
                Logger.LogVerbose(ModuleName, $"Sending priority command: {priorityToSend}");
                _connection.Send(priorityToSend);
            }

            if (!string.IsNullOrEmpty(message))
                _connection.Send(message);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _timerRegistration.Dispose(); } catch { }

            _initCompletionSource?.TrySetCanceled();

            _connection.DataReceived -= OnDataReceived;

            if (_initTimer != null)
            {
                _initTimer.Elapsed -= OnInitTimerElapsed;
                _initTimer.Stop();
                _initTimer.Dispose();
                _initTimer = null;
            }

            if (_pollTimer != null)
            {
                _pollTimer.Elapsed -= OnPollTimerElapsed;
                _pollTimer.Stop();
                _pollTimer.Dispose();
                _pollTimer = null;
            }

            if (_pttWatchdogTimer != null)
            {
                _pttWatchdogTimer.Elapsed -= OnPttWatchdogTimerElapsed;
                _pttWatchdogTimer.Stop();
                _pttWatchdogTimer.Dispose();
                _pttWatchdogTimer = null;
            }
        }
    }
}
