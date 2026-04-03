#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PgTg.AMP;
using PgTg.Common;
using PgTg.Plugins;
using PgTg.Plugins.Core;
using SPEAmpTunerPlugin.MyModel.Internal;

namespace SPEAmpTunerPlugin.MyModel
{
    /// <summary>SPE Expert linear amplifier + ATU — serial binary protocol with internal fictitious <c>$…;</c> pipeline.</summary>
    [PluginInfo("spe.expert.amptuner", "SPE Expert Amplifier+ATU",
        Version = "1.0.0",
        Manufacturer = "SPE (via PgTg plugin)",
        Capability = PluginCapability.AmplifierAndTuner,
        Description = "SPE Expert amplifier and ATU control over USB/serial (no TCP/WOL; band from CAT frequency only)",
        UiSections = PluginUiSection.Serial | PluginUiSection.Reconnect)]
    public class SPEAmpTunerPlugin : IAmplifierTunerPlugin
    {
        public const string PluginId = "spe.expert.amptuner";
        private const string ModuleName = "SPEAmpTunerPlugin";

        private readonly CancellationToken _cancellationToken;

        private ISpeAmpTunerConnection? _connection;
        private CommandQueue? _commandQueue;
        private ResponseParser? _parser;
        private StatusTracker? _statusTracker;
        private SPEAmpTunerConfiguration? _config;

        private bool _stopped;
        private bool _disposed;
        private bool _disableControlsOnDisconnect = true;
        private int _lastBandSent = -1;
        private PluginConnectionState _lastReportedConnectionState = PluginConnectionState.Disconnected;

        #region IDevicePlugin

        public PluginInfo Info { get; } = new PluginInfo
        {
            Id = PluginId,
            Name = "SPE Expert Amplifier+ATU",
            Version = "1.0.0",
            Manufacturer = "SPE (via PgTg plugin)",
            Capability = PluginCapability.AmplifierAndTuner,
            Description = "SPE Expert amplifier and ATU over serial (see SPE Application Programmer's Guide for frame details).",
            ConfigurationType = typeof(SPEAmpTunerConfiguration),
            UiSections = PluginUiSection.Serial | PluginUiSection.Reconnect
        };

        public PluginConnectionState ConnectionState => _connection?.ConnectionState ?? PluginConnectionState.Disconnected;

        public double MeterDisplayMaxPower => Constants.MeterDisplayMaxPower;

        public bool DisableControlsOnDisconnect => _disableControlsOnDisconnect;

        public event EventHandler<PluginConnectionStateChangedEventArgs>? ConnectionStateChanged;
        public event EventHandler<MeterDataEventArgs>? MeterDataAvailable;
        public event EventHandler? DeviceDataChanged;

        #endregion

        public event EventHandler<AmplifierStatusEventArgs>? StatusChanged;
        public event EventHandler<TunerStatusEventArgs>? TunerStatusChanged;

        public SPEAmpTunerPlugin(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
        }

        #region Lifecycle

        public Task InitializeAsync(IPluginConfiguration configuration, CancellationToken cancellationToken)
        {
            _config = configuration as SPEAmpTunerConfiguration ?? new SPEAmpTunerConfiguration
            {
                IpAddress = configuration.IpAddress,
                Port = configuration.Port,
                Enabled = configuration.Enabled,
                ReconnectDelayMs = configuration.ReconnectDelayMs,
                ConnectionType = PluginConnectionType.Serial,
                SerialPort = configuration.SerialPort,
                BaudRate = configuration.BaudRate
            };

            if (_config.ConnectionType != PluginConnectionType.Serial)
            {
                Logger.LogInfo(ModuleName, "SPE plugin is serial-only; forcing Serial connection type.");
                _config.ConnectionType = PluginConnectionType.Serial;
            }

            var serialConnection = new SerialConnection(_cancellationToken);
            serialConnection.Configure(_config.SerialPort, _config.BaudRate);
            _connection = serialConnection;
            Logger.LogInfo(ModuleName, $"Using serial connection: {_config.SerialPort} at {_config.BaudRate} baud");

            _commandQueue = new CommandQueue(_connection, _cancellationToken);
            _parser = new ResponseParser();
            _statusTracker = new StatusTracker();

            _commandQueue.Configure(
                _config.PollingIntervalRxMs,
                _config.PollingIntervalTxMs,
                _config.PttWatchdogIntervalMs);
            _commandQueue.SkipDeviceWakeup = _config.SkipDeviceWakeup;

            _disableControlsOnDisconnect = _config.DisableControlsOnDisconnect;

            _connection.DataReceived += OnDataReceived;
            _connection.ConnectionStateChanged += OnConnectionStateChanged;

            return Task.CompletedTask;
        }

        public async Task StartAsync()
        {
            Logger.LogVerbose(ModuleName, "StartAsync");
            if (_connection == null || _commandQueue == null || _config == null)
                throw new InvalidOperationException("Plugin not initialized");

            if (_cancellationToken.IsCancellationRequested)
            {
                Logger.LogInfo(ModuleName, "Plugin startup cancelled before start");
                return;
            }

            await _connection.StartAsync();

            if (_cancellationToken.IsCancellationRequested)
            {
                _connection.Stop();
                Logger.LogInfo(ModuleName, "Plugin startup cancelled after connection start");
                return;
            }

            await _commandQueue.StartAsync();

            if (!_cancellationToken.IsCancellationRequested)
                Logger.LogInfo(ModuleName, "Plugin started");
            else
                Logger.LogInfo(ModuleName, "Plugin startup cancelled after commandQueue start");
        }

        public async Task StopAsync()
        {
            if (_stopped) return;

            Logger.LogInfo(ModuleName, "Stopping plugin");

            _statusTracker?.ZeroMeterValues();
            RaiseMeterDataEvent();

            _commandQueue?.Stop();

            if (_connection != null)
            {
                _connection.DataReceived -= OnDataReceived;
                _connection.ConnectionStateChanged -= OnConnectionStateChanged;
                _connection.Stop();
            }

            _stopped = true;
            Logger.LogInfo(ModuleName, "Plugin stopped");

            await Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (!_stopped)
            {
                try
                {
                    StopAsync().Wait(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    Logger.LogError(ModuleName, $"Error in StopAsync during Dispose: {ex.Message}");
                }
            }

            _commandQueue?.Dispose();

            if (_connection != null)
            {
                _connection.DataReceived -= OnDataReceived;
                _connection.ConnectionStateChanged -= OnConnectionStateChanged;
                _connection.Stop();
                _connection.Dispose();
            }
        }

        #endregion

        #region IDevicePlugin Wakeup/Shutdown

        public async Task WakeupDeviceAsync()
        {
            if (_connection?.IsConnected == true && _commandQueue != null)
            {
                Logger.LogInfo(ModuleName, "WakeupDeviceAsync: starting device initialization");
                await _commandQueue.InitializeDeviceAsync();
            }
        }

        public Task ShutdownDeviceAsync()
        {
            if (_connection?.IsConnected == true)
            {
                _connection.Send(Constants.ShutdownCmd);
                Logger.LogInfo(ModuleName, "ShutdownDeviceAsync: sent ShutdownCmd");
            }
            return Task.CompletedTask;
        }

        #endregion

        #region IAmplifierPlugin

        public AmplifierStatusData GetStatus()
        {
            return _statusTracker?.GetAmplifierStatus() ?? new AmplifierStatusData();
        }

        public void SendPriorityCommand(AmpCommand command)
        {
            if (_commandQueue == null || _statusTracker == null) return;

            _commandQueue.SendPriorityCommand(command, _statusTracker.AmpState);
        }

        public void SetFrequencyKhz(int frequencyKhz)
        {
            int band = SpeBandLookup.DeriveBandIndex(frequencyKhz);
            if (band < 0) return;
            if (band == _lastBandSent) return;
            _lastBandSent = band;
            _commandQueue?.SetBandIndex(band);
        }

        public void SetRadioConnected(bool connected)
        {
            if (!connected && _commandQueue != null)
            {
                _commandQueue.ForceReleasesPtt();
                Logger.LogVerbose(ModuleName, "Radio disconnected, forcing device to RX (Safety Measure)");
            }
        }

        public void SetOperateMode(bool operate)
        {
            if (_commandQueue == null) return;

            _commandQueue.SetOperateMode(operate);
            Logger.LogVerbose(ModuleName, $"Setting amplifier to {(operate ? "OPERATE" : "STANDBY")} mode");
        }

        public void SetRadioPtt(bool isPtt)
        {
            if (_statusTracker != null && _statusTracker.SetRadioPtt(isPtt))
                _commandQueue?.OnPttStateChanged(isPtt);
        }

        public void SetTransmitMode(string mode)
        {
        }

        #endregion

        #region ITunerPlugin

        public TunerStatusData GetTunerStatus()
        {
            return _statusTracker?.GetTunerStatus() ?? new TunerStatusData();
        }

        public void SetInline(bool inline)
        {
            _commandQueue?.SetTunerInline(inline);
        }

        public void StartTune()
        {
            _commandQueue?.SetTuneStart(true);
        }

        public void StopTune()
        {
            _commandQueue?.SetTuneStart(false);
        }

        #endregion

        #region Event Handlers

        private void OnDataReceived(string data)
        {
            if (_parser == null || _statusTracker == null || _commandQueue == null) return;

            var update = _parser.Parse(data, _statusTracker);

            if (update.IsPtt.HasValue)
                _commandQueue.OnTxRxResponseReceived(update.IsPtt.Value);

            if (update.FirmwareVersion.HasValue)
                _commandQueue.SetFirmwareVersion(update.FirmwareVersion.Value);

            bool hadAmpChange = update.AmpStateChanged || update.PttStateChanged || update.PttReady;
            bool hadTunerChange = update.TunerStateChanged || update.TuningStateChanged || update.TunerRelaysChanged;
            bool hadDeviceDataChange = update.AmpStateChanged || update.TunerStateChanged
                || update.FaultCode.HasValue || update.BandNumber.HasValue || update.Antenna.HasValue
                || update.Input.HasValue || update.PowerLevel != null;

            _statusTracker.ApplyUpdate(update);

            if (update.IsPtt.HasValue)
                _commandQueue.OnPttStateChanged(update.IsPtt.Value);

            if (update.TuningState.HasValue)
                _commandQueue.OnTuningStateChanged(update.TuningState.Value == TunerTuningState.TuningInProgress);

            if (hadAmpChange)
            {
                var ampStatus = _statusTracker.GetAmplifierStatus();
                ampStatus.WhatChanged = DetermineAmpChange(update);
                StatusChanged?.Invoke(this, new AmplifierStatusEventArgs(ampStatus, PluginId));
            }

            if (hadTunerChange)
            {
                var tunerStatus = _statusTracker.GetTunerStatus();
                tunerStatus.WhatChanged = DetermineTunerChange(update);
                TunerStatusChanged?.Invoke(this, new TunerStatusEventArgs(tunerStatus, PluginId));
            }

            if (hadDeviceDataChange)
                DeviceDataChanged?.Invoke(this, EventArgs.Empty);

            RaiseMeterDataEvent();
        }

        private void OnConnectionStateChanged(PluginConnectionState state)
        {
            var previous = _lastReportedConnectionState;
            _lastReportedConnectionState = state;
            _statusTracker?.SetPluginConnectionState(state);
            ConnectionStateChanged?.Invoke(this, new PluginConnectionStateChangedEventArgs(previous, state));

            DeviceDataChanged?.Invoke(this, EventArgs.Empty);

            if (state == PluginConnectionState.Connected)
                Logger.LogInfo(ModuleName, "Connected to device");
            else if (state == PluginConnectionState.Disconnected)
                Logger.LogInfo(ModuleName, "Disconnected from device");
        }

        private void RaiseMeterDataEvent()
        {
            if (_statusTracker == null) return;

            var readings = _statusTracker.GetMeterReadings();
            bool isTransmitting = _statusTracker.IsPtt || _statusTracker.RadioPtt;
            var args = new MeterDataEventArgs(readings, isTransmitting, PluginId);
            MeterDataAvailable?.Invoke(this, args);
        }

        #endregion

        #region IDevicePlugin Device Control

        public Dictionary<string, object> GetDeviceData()
        {
            return _statusTracker?.GetDeviceData() ?? new Dictionary<string, object>();
        }

        public bool SendDeviceCommand(string command)
        {
            if (_connection == null || !_connection.IsConnected) return false;
            _connection.Send(command);
            return true;
        }

        // Use PgTg.Web.* fully qualified — newer bridges also expose the same names under PgTg.Common (CS0104 if imported).
        public PgTg.Web.DeviceControlDefinition? GetDeviceControlDefinition()
        {
            return new PgTg.Web.DeviceControlDefinition
            {
                Elements = new List<PgTg.Web.DeviceControlElement>
                {
                    new PgTg.Web.DeviceControlElement
                    {
                        ActiveColor = "green",
                        InactiveColor = "gray",
                        ActiveText = "Power",
                        InactiveText = "Power",
                        ActiveCommand = Constants.SwitchToggleCmd,
                        InactiveCommand = Constants.SwitchToggleCmd,
                        ResponseKey = "ON",
                        ActiveValue = "1",
                        IsClickable = true,
                        IsPowerIndicator = true
                    },
                    new PgTg.Web.DeviceControlElement
                    {
                        ActiveColor = "green",
                        InactiveColor = "yellow",
                        ActiveText = "Operate",
                        InactiveText = "Standby",
                        ActiveCommand = Constants.StandbyCmd,
                        InactiveCommand = Constants.OperateCmd,
                        ResponseKey = "OS",
                        ActiveValue = "1",
                        IsClickable = true
                    },
                    new PgTg.Web.DeviceControlElement
                    {
                        ActiveColor = "green",
                        InactiveColor = "gray",
                        ActiveText = "Ant 1",
                        InactiveText = "Ant 1",
                        ActiveCommand = Constants.Antenna1Cmd,
                        InactiveCommand = Constants.Antenna1Cmd,
                        ResponseKey = "AN",
                        ActiveValue = "1",
                        IsClickable = true
                    },
                    new PgTg.Web.DeviceControlElement
                    {
                        ActiveColor = "green",
                        InactiveColor = "gray",
                        ActiveText = "Ant 2",
                        InactiveText = "Ant 2",
                        ActiveCommand = Constants.Antenna2Cmd,
                        InactiveCommand = Constants.Antenna2Cmd,
                        ResponseKey = "AN",
                        ActiveValue = "2",
                        IsClickable = true
                    },
                    new PgTg.Web.DeviceControlElement
                    {
                        ActiveColor = "green",
                        InactiveColor = "gray",
                        ActiveText = "Ant 3",
                        InactiveText = "Ant 3",
                        ActiveCommand = Constants.Antenna3Cmd,
                        InactiveCommand = Constants.Antenna3Cmd,
                        ResponseKey = "AN",
                        ActiveValue = "3",
                        IsClickable = true
                    },
                    new PgTg.Web.DeviceControlElement
                    {
                        ActiveColor = "green",
                        InactiveColor = "gray",
                        ActiveText = "Ant 4",
                        InactiveText = "Ant 4",
                        ActiveCommand = Constants.Antenna4Cmd,
                        InactiveCommand = Constants.Antenna4Cmd,
                        ResponseKey = "AN",
                        ActiveValue = "4",
                        IsClickable = true
                    },
                    new PgTg.Web.DeviceControlElement
                    {
                        ActiveColor = "green",
                        InactiveColor = "gray",
                        ActiveText = "Input 1",
                        InactiveText = "Input 1",
                        ActiveCommand = Constants.Input1Cmd,
                        InactiveCommand = Constants.Input1Cmd,
                        ResponseKey = "IN",
                        ActiveValue = "1",
                        IsClickable = true
                    },
                    new PgTg.Web.DeviceControlElement
                    {
                        ActiveColor = "green",
                        InactiveColor = "gray",
                        ActiveText = "Input 2",
                        InactiveText = "Input 2",
                        ActiveCommand = Constants.Input2Cmd,
                        InactiveCommand = Constants.Input2Cmd,
                        ResponseKey = "IN",
                        ActiveValue = "2",
                        IsClickable = true
                    },
                    new PgTg.Web.DeviceControlElement
                    {
                        ActiveColor = "green",
                        InactiveColor = "yellow",
                        ActiveText = "Inline",
                        InactiveText = "Bypass",
                        ActiveCommand = "$AI0;",
                        InactiveCommand = "$AI1;",
                        ResponseKey = "AI",
                        ActiveValue = "1",
                        IsClickable = false
                    },
                    new PgTg.Web.DeviceControlElement
                    {
                        ActiveColor = "red",
                        InactiveColor = "gray",
                        ActiveText = "FAULT",
                        InactiveText = "Fault",
                        ActiveCommand = Constants.ClearFaultCmd,
                        InactiveCommand = null,
                        ResponseKey = "FL",
                        ActiveValue = "1",
                        IsClickable = true
                    },
                    new PgTg.Web.DeviceControlElement
                    {
                        ActiveColor = "orange",
                        InactiveColor = "gray",
                        ActiveText = "PWR LOW",
                        InactiveText = "Pwr Low",
                        ActiveCommand = Constants.SpePowerLevelCmd,
                        InactiveCommand = Constants.SpePowerLevelCmd,
                        ResponseKey = "PL",
                        ActiveValue = "L",
                        IsClickable = true
                    },
                    new PgTg.Web.DeviceControlElement
                    {
                        ActiveColor = "yellow",
                        InactiveColor = "gray",
                        ActiveText = "PWR MID",
                        InactiveText = "Pwr Mid",
                        ActiveCommand = Constants.SpePowerLevelCmd,
                        InactiveCommand = Constants.SpePowerLevelCmd,
                        ResponseKey = "PL",
                        ActiveValue = "M",
                        IsClickable = true
                    },
                    new PgTg.Web.DeviceControlElement
                    {
                        ActiveColor = "red",
                        InactiveColor = "gray",
                        ActiveText = "PWR HIGH",
                        InactiveText = "Pwr High",
                        ActiveCommand = Constants.SpePowerLevelCmd,
                        InactiveCommand = Constants.SpePowerLevelCmd,
                        ResponseKey = "PL",
                        ActiveValue = "H",
                        IsClickable = true
                    }
                },
                // Fan row omitted per plan (no SPE fan control in this integration).
                FanControl = null
            };
        }

        #endregion

        #region Helpers

        private static AmplifierStatusChange DetermineAmpChange(ResponseParser.StatusUpdate update)
        {
            if (update.PttReady) return AmplifierStatusChange.PttReady;
            if (update.PttStateChanged) return AmplifierStatusChange.PttStateChanged;
            if (update.AmpStateChanged) return AmplifierStatusChange.OperateStateChanged;
            return AmplifierStatusChange.General;
        }

        private static TunerStatusChange DetermineTunerChange(ResponseParser.StatusUpdate update)
        {
            if (update.TuningStateChanged) return TunerStatusChange.TuningStateChanged;
            if (update.TunerStateChanged) return TunerStatusChange.OperateStateChanged;
            if (update.TunerRelaysChanged) return TunerStatusChange.RelayValuesChanged;
            return TunerStatusChange.General;
        }

        #endregion
    }
}
