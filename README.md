# SPEAmpTunerPlugin

PgTgBridge plugin for **SPE Expert** linear amplifiers with integrated ATU. Communicates with the amplifier over **USB serial only** (no TCP/WOL to the SPE). CAT frequency from the radio is **not** sent to the SPE; the plugin derives **HF band (0–10)** from `SetFrequencyKhz` and sends SPE band selection only.

## References

- [SPE Application Programmer’s Guide (PDF)](https://www.spetlc.com/images/download/SPE_Application_Programmers_Guide.pdf)
- [PgTgBridge Plugin Programmer’s Reference](https://github.com/KD4Z/PgTgSamplePlugins/blob/main/Documentation/PgTgBridge-Plugin-Programmers-Reference.md)

## Build

1. Install **PgTgBridge** and ensure `PgTg.dll`, `PgTg.Common.dll`, and `PgTg.Helpers.dll` are available (default hint path: `C:\Program Files\PgTgBridge\bin\`).
2. If your bridge SDK already ships **Device Control** types under `PgTg.Web`, **delete** `MyModel/Internal/PgTgDeviceControlStubs.cs` to avoid duplicate type definitions.
3. From the repo root: `dotnet build SPEAmpTunerPlugin.sln`

## Serial + emulator smoke test

1. Install a virtual null-modem pair (e.g. **com0com**) so two COM ports are linked (e.g. COM1 ↔ COM2).
2. Run the emulator on one end (from the repository root):  
   `dotnet run --project SPEAmpTunerEmulator -- COM2 38400`
3. Point the plugin at the other port (COM1) in PgTgBridge plugin settings.

The emulator speaks the same framed protocol as `SpeFrameCodec` / `SpeCommandTranslator` (used for development when the official PDF byte layout is mapped into those types).

## Limitations (see plan)

- **4 m** band is not implemented.
- **TCP / WOL** are not used for the SPE.
- **Inductor/cap** relay values are not exposed by SPE; parser reports `0`.
- **Fan** row is omitted in Device Control (`FanControl = null`).

## Manual test ideas

Connect, init, poll meters, device control (power toggle, operate/standby, antennas 1–4, inputs 1–2, fault clear, PWR L/M/H), PTT path, tune path, band change from CAT frequency.
