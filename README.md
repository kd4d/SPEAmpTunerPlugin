# SPEAmpTunerPlugin

PgTgBridge plugin for **SPE Expert** linear amplifiers with integrated ATU. Communicates with the amplifier over **USB serial only** (no TCP/WOL to the SPE). CAT frequency from the radio is **not** sent to the SPE; the plugin derives **HF band (0–10)** from `SetFrequencyKhz` and sends SPE **BAND− / BAND+** key frames until the reported band matches.

## Protocol (SPE Application Programmer’s Guide)

- **Host → amplifier:** 6-byte frames with **`0x55 0x55 0x55`** sync (see `SpeProtocol.cs`). Status polling uses command **`0x90`** (`StatusPoll`).
- **Amplifier → host:** comma-separated **status line** (typically **CRLF**-terminated). Some firmware may wrap the 67-byte CSV in a **`0xAA`**-sync packet; `SerialConnection` accepts both framed and plain CSV lines.
- **Default baud rate** is **9600** (per SPE documentation); match the amplifier or USB adapter.

Internal **`SpeFrameCodec` / `SpeCommandTranslator`** types remain in the project as reference/emulation helpers; the **live** serial path uses **`SpeProtocol`** + **`SpeCsvStatusParser`**.

## References

- [SPE Application Programmer’s Guide (PDF)](https://www.spetlc.com/images/download/SPE_Application_Programmers_Guide.pdf)
- [PgTgBridge Plugin Programmer’s Reference](https://github.com/KD4Z/PgTgSamplePlugins/blob/main/Documentation/PgTgBridge-Plugin-Programmers-Reference.md)

## Build

The plugin `.csproj` lives next to the `SPEAmpTunerEmulator` folder; the project file **excludes** that folder so the SDK does not compile emulator sources into the plugin (which would break the build and cause CS0006 / missing `SPEAmpTunerPlugin.dll` for the emulator).

1. Install **PgTgBridge** and ensure `PgTg.dll`, `PgTg.Common.dll`, and `PgTg.Helpers.dll` are available (default hint path: `C:\Program Files\PgTgBridge\bin\`).
2. If your bridge SDK already ships **Device Control** types under `PgTg.Web`, **delete** `MyModel/Internal/PgTgDeviceControlStubs.cs` to avoid clashing with the real assemblies.
3. **CS0104** (*ambiguous `DeviceControlDefinition` between `PgTg.Web` and `PgTg.Common`*): the plugin uses **fully qualified** `PgTg.Web.*` in `GetDeviceControlDefinition` only (no `using` for those types). If your bridge’s `IDevicePlugin` contract expects **`PgTg.Common`** types instead, replace `PgTg.Web` with `PgTg.Common` in that method’s return type and object initializers.
4. From the repo root: `dotnet build SPEAmpTunerPlugin.sln`

## Serial + emulator smoke test

1. Install a virtual null-modem pair (e.g. **com0com**) so two COM ports are linked (e.g. COM1 ↔ COM2).
2. Run the emulator on one end (from the repository root):  
   `dotnet run --project SPEAmpTunerEmulator -- COM2 9600`
3. Point the plugin at the other port (COM1) in PgTgBridge plugin settings (baud **9600** unless you changed it).

The emulator detects **`0x55 0x55 0x55 0x01 0x90 0x90`** status polls and replies with a synthetic **CSV** line (same general shape as [kd4d/SPEExpert](https://github.com/kd4d/SPEExpert) `SpeEmulator`).

## Limitations (see plan)

- **4 m** band is not implemented.
- **TCP / WOL** are not used for the SPE.
- **Software PTT** (`AmpCommand` TX/RX) is not mapped to SPE keyboard frames; use **RF/hardware PTT** as with the vendor stack.
- **Inductor/cap** relay values are not exposed by SPE; parser reports `0`.
- **Fan** row is omitted in Device Control (`FanControl = null`).

## Manual test ideas

Connect, init, poll meters, device control (power toggle, operate/standby, antennas 1–4, inputs 1–2, fault clear, PWR L/M/H), PTT path, tune path, band change from CAT frequency.
