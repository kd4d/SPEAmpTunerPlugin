#nullable enable

using System;
using System.Collections.Generic;
using PgTg.AMP;
using PgTg.Common;
using PgTg.Plugins.Core;
using MeterUnits = PgTg.RADIO.MeterUnits;

namespace SPEAmpTunerPlugin.MyModel.Internal
{
    internal class StatusTracker
    {
        private const string ModuleName = "StatusTracker";
        private readonly object _lock = new();

        public AmpOperateState AmpState { get; private set; } = AmpOperateState.Unknown;
        public bool IsPtt { get; private set; }
        public bool RadioPtt { get; private set; }

        public double ForwardPower { get; private set; }
        public double SWR { get; private set; } = 1.0;
        public double ReturnLoss { get; private set; } = 99;
        public int Temperature { get; private set; }
        public double Voltage { get; private set; }
        public double Current { get; private set; }
        public int BandNumber { get; private set; }
        public string BandName { get; private set; } = string.Empty;
        public int FaultCode { get; private set; }
        public string SerialNumber { get; private set; } = string.Empty;
        public double FirmwareVersion { get; private set; }
        public bool IsVitaDataPopulated { get; private set; }

        public TunerOperateState TunerState { get; private set; } = TunerOperateState.Unknown;
        public TunerTuningState TuningState { get; private set; } = TunerTuningState.Unknown;
        public int InductorValue { get; private set; }
        public int CapacitorValue { get; private set; }
        public int Antenna { get; private set; }
        public int Input { get; private set; } = 1;
        public string PowerLevel { get; private set; } = "M";
        public double TunerSWR { get; private set; } = 1.0;
        public int VFWD { get; private set; }

        public PluginConnectionState PluginConnectionState { get; private set; } = PluginConnectionState.Disconnected;

        public void SetPluginConnectionState(PluginConnectionState state)
        {
            lock (_lock)
            {
                PluginConnectionState = state;
            }
        }

        /// <summary>SPE alarm codes — extend from the official SPE PDF as needed.</summary>
        public static string GetFaultDescription(int faultCode) => faultCode switch
        {
            0 => string.Empty,
            1 => "Antenna / SWR",
            2 => "Temperature",
            3 => "Power supply",
            4 => "Interlock",
            5 => "Driver overdrive",
            6 => "Internal",
            _ => $"SPE alarm {faultCode}"
        };

        /// <summary>Tuner forward power tracks amplifier forward power (SPE does not expose separate tuner FWD).</summary>
        private double TunerForwardPowerWatts => ForwardPower;

        public void ApplyUpdate(ResponseParser.StatusUpdate update)
        {
            lock (_lock)
            {
                if (update.AmpState.HasValue) AmpState = update.AmpState.Value;
                if (update.IsPtt.HasValue) IsPtt = update.IsPtt.Value;
                if (update.ForwardPower.HasValue) ForwardPower = update.ForwardPower.Value;
                if (update.SWR.HasValue) SWR = update.SWR.Value;
                if (update.ReturnLoss.HasValue) ReturnLoss = update.ReturnLoss.Value;
                if (update.Temperature.HasValue) Temperature = update.Temperature.Value;
                if (update.Voltage.HasValue) Voltage = update.Voltage.Value;
                if (update.Current.HasValue) Current = update.Current.Value;
                if (update.BandNumber.HasValue) BandNumber = update.BandNumber.Value;
                if (update.BandName != null) BandName = update.BandName;
                if (update.FaultCode.HasValue) FaultCode = update.FaultCode.Value;
                if (update.SerialNumber != null) SerialNumber = update.SerialNumber;
                if (update.FirmwareVersion.HasValue) FirmwareVersion = update.FirmwareVersion.Value;
                if (update.IsVitaDataPopulated) IsVitaDataPopulated = true;

                if (update.TunerState.HasValue) TunerState = update.TunerState.Value;
                if (update.TuningState.HasValue) TuningState = update.TuningState.Value;
                if (update.InductorValue.HasValue) InductorValue = update.InductorValue.Value;
                if (update.CapacitorValue.HasValue) CapacitorValue = update.CapacitorValue.Value;
                if (update.Antenna.HasValue) Antenna = update.Antenna.Value;
                if (update.Input.HasValue) Input = update.Input.Value;
                if (update.PowerLevel != null) PowerLevel = update.PowerLevel;
                if (update.TunerSWR.HasValue) TunerSWR = update.TunerSWR.Value;
                if (update.VFWD.HasValue) VFWD = update.VFWD.Value;
            }
        }

        public AmplifierStatusData GetAmplifierStatus()
        {
            lock (_lock)
            {
                return new AmplifierStatusData
                {
                    OperateState = AmpState,
                    IsPttActive = IsPtt,
                    BandNumber = BandNumber,
                    BandName = BandName,
                    FaultCode = FaultCode,
                    FirmwareVersion = FirmwareVersion.ToString("F2"),
                    SerialNumber = SerialNumber,
                    ForwardPower = ForwardPower,
                    SWR = SWR,
                    ReturnLoss = ReturnLoss,
                    Temperature = Temperature
                };
            }
        }

        public TunerStatusData GetTunerStatus()
        {
            lock (_lock)
            {
                return new TunerStatusData
                {
                    OperateState = TunerState,
                    TuningState = TuningState,
                    InductorValue = InductorValue,
                    Capacitor1Value = CapacitorValue,
                    Capacitor2Value = 0,
                    LastSwr = TunerSWR,
                    FirmwareVersion = FirmwareVersion.ToString("F2"),
                    SerialNumber = SerialNumber,
                    ForwardPower = TunerForwardPowerWatts
                };
            }
        }

        public Dictionary<MeterType, MeterReading> GetMeterReadings()
        {
            lock (_lock)
            {
                bool isTransmitting = RadioPtt || IsPtt;

                double currentFwdPower = isTransmitting ? ForwardPower : 0;
                double currentSwr = isTransmitting ? SWR : 1.0;
                double currentReturnLoss = isTransmitting ? ReturnLoss : 99;
                double currentTunerFwdPower = isTransmitting ? TunerForwardPowerWatts : 0;
                double currentTunerSwr = isTransmitting ? TunerSWR : 1.0;

                return new Dictionary<MeterType, MeterReading>
                {
                    [MeterType.ForwardPower] = new MeterReading(MeterType.ForwardPower, currentFwdPower, MeterUnits.Watts),
                    [MeterType.SWR] = new MeterReading(MeterType.SWR, currentSwr, MeterUnits.SWR),
                    [MeterType.ReturnLoss] = new MeterReading(MeterType.ReturnLoss, currentReturnLoss, MeterUnits.Db),
                    [MeterType.Temperature] = new MeterReading(MeterType.Temperature, Temperature, MeterUnits.DegreesC),
                    [MeterType.TunerForwardPower] = new MeterReading(MeterType.TunerForwardPower, currentTunerFwdPower, MeterUnits.Watts),
                    [MeterType.TunerSWR] = new MeterReading(MeterType.TunerSWR, currentTunerSwr, MeterUnits.SWR),
                    [MeterType.TunerReturnLoss] = new MeterReading(MeterType.TunerReturnLoss, currentReturnLoss, MeterUnits.Db)
                };
            }
        }

        public Dictionary<string, object> GetDeviceData()
        {
            lock (_lock)
            {
                bool connected = PluginConnectionState == PluginConnectionState.Connected;
                return new Dictionary<string, object>
                {
                    ["ON"] = connected ? 1 : 0,
                    ["OS"] = AmpState == AmpOperateState.Operate || AmpState == AmpOperateState.Transmit ? 1 : 0,
                    ["AN"] = Antenna,
                    ["AI"] = TunerState == TunerOperateState.Inline ? 1 : 0,
                    ["FL"] = FaultCode > 0 ? 1 : 0,
                    ["FaultDesc"] = GetFaultDescription(FaultCode),
                    ["BN"] = BandNumber,
                    ["IN"] = Input,
                    ["PL"] = PowerLevel
                };
            }
        }

        public void ZeroMeterValues()
        {
            lock (_lock)
            {
                ForwardPower = 0;
                SWR = 1.0;
                ReturnLoss = 99;
                Temperature = 0;
                TunerSWR = 1.0;
                VFWD = 0;
            }
        }

        public bool SetRadioPtt(bool isPtt)
        {
            lock (_lock)
            {
                if (RadioPtt != isPtt)
                {
                    Logger.LogVerbose(ModuleName, $"RadioPtt changed: {RadioPtt} -> {isPtt}");
                    RadioPtt = isPtt;
                    return true;
                }
            }
            return false;
        }
    }
}
