# Joystick Input Tuner

Desktop WPF tool for joystick input diagnostics and filtering (focus on yaw-axis jitter/spikes).

**Current pre-release build:** `1.0.0 Build PR-R2P34` (shown in app title and main window after local build).

## Getting the app

This repository does **not** include `Portable/JoystickInputTuner.App.exe` (~140 MB single-file publish exceeds GitHub file limits).

**Included in repo:** `Portable/vJoyInterface.dll` (required next to the exe at runtime).

**Build locally:**

```powershell
dotnet build .\src\JoystickInputTuner.App\JoystickInputTuner.App.csproj -c Release
```

Output: `Portable\JoystickInputTuner.App.exe` + `Portable\vJoyInterface.dll`. User data: `Portable\_Data\` (created at runtime).

Requires Windows and .NET 10 SDK.

## Features

- Device and axis selection (including dynamic axis detection).
- Real-time **Monitor**: raw vs filtered for the stream axis; **overlay** other device axes (semi-transparent lines).
- **Per-axis toggles** on Monitor to show/hide overlay lines (X, Y, Z, RX, RY, RZ, SL0, SL1).
- Filter pipeline: Deadzone, Median, Hampel, Spike Gate (radial zones, **RailHold**, **Ultra-Spike**, **Swing Bypass**), **Output Settle**, Z Impulse Guard, Cross-Axis Shield (hard lock + intent), Rate Limiter, EMA.
- **Filter session** — last UI filter state saved to `_Data/filters.json` (debounced; restored on next launch after profile auto-apply).
- Profile save/load (JSON) — full device + filter snapshot via **Save profile**.
- Embedded **T.A320 Pilot** default profile (portable seed).
- RU/EN interface.
- Startup agent mode (`--agent`) for auto-apply workflow.
- **vJoy virtual device output** — only the selected axis is sent to the virtual joystick (other vJoy axes stay centered); busy-device recovery hints.
- Diagnostics log: movement, settings, **chartStream** / **chartOverlay** and values for selected monitor axes; optional **clear on startup** and **Clear log** in Settings.

## vJoy Output

1. Install [vJoy](https://sourceforge.net/projects/vjoy/) (recommended: Brunner vJoy 2.2.x) and enable **Device #1** in *Configure vJoy*.
2. In the app, select output **vJoy Virtual Device** and press **Start**.
3. In the game, bind yaw (or the tuned axis) to the matching axis on the **vJoy Device** (not the physical stick).

The physical device is read by this app; the game should use the vJoy device for the filtered axis.

## Data Storage

Application data is stored near the executable in:

- `_Data/appsettings.json` — UI language, logging, auto-apply, **ResetLogOnStartup**
- `_Data/profiles/*.json` — named profiles (Save profile)
- `_Data/filters.json` — last filter UI session (soft restore; not seeded on first portable run)
- `_Data/logs/tuner_YYYYMMDD.log` — movement, monitor, settings, chart axis selection

If legacy data exists in `%LocalAppData%\JoystickInputTuner`, it is migrated automatically on first run.

## Portable publish (optional)

```powershell
dotnet publish .\src\JoystickInputTuner.App\JoystickInputTuner.App.csproj -c Release
```

Self-contained single-file `win-x64`. Release **build** also refreshes `Portable/` (exe is gitignored).
