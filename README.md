# Joystick Input Tuner

Desktop WPF tool for joystick input diagnostics and filtering (focus on yaw-axis jitter/spikes).

**Current pre-release build:** `2.1.3 Build PR-R3P1` (shown in app title and main window after local build).

**Target release:** `2.1.3`

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
- **Axis bind lock** — center the stream axis via joystick / keyboard / mouse binding (toggle on press).
- **Filter session v3** — `_Data/filters.json` saves filters, device/axis/polling, calibration, monitor toggles, and UI preferences.
- Profile save/load (JSON) — full device + filter snapshot via **Save profile**.
- Embedded **T.A320 Pilot** default profile (portable seed).
- RU/EN interface.
- **Background agent** (`--agent`) and **handoff** when closing the UI while streaming (after **Apply** + **Start**).
- Windows **startup agent** (optional) for auto-resume after reboot.
- **vJoy virtual device output** — only the selected axis is sent to the virtual joystick; busy-device recovery.
- Diagnostics log: movement, settings, chart axes; optional **clear on startup** and **Clear log** in Settings.

## vJoy Output

1. Install [vJoy](https://sourceforge.net/projects/vjoy/) (recommended: Brunner vJoy 2.2.x) and enable **Device #1** in *Configure vJoy*.
2. In the app, select output **vJoy Virtual Device**, press **Apply** on your profile, then **Start**.
3. In the game, bind yaw (or the tuned axis) to the matching axis on the **vJoy Device** (not the physical stick).

## Handoff to background agent

1. **Apply** a profile and **Start** the stream.
2. Close the main window — the app saves state and starts a hidden `--agent` process (~1.5 s delay) to keep filtering.
3. Check `{exeDir}\_Data\logs\tuner_YYYYMMDD.log` for `agent-handoff` and `stream-start`.

## Data Storage

Application data is stored near the executable in:

- `_Data/appsettings.json` — UI language, logging, auto-apply, agent resume, **ResetLogOnStartup**
- `_Data/profiles/*.json` — named profiles
- `_Data/filters.json` — filter session v3
- `_Data/logs/tuner_YYYYMMDD.log` — diagnostics

Legacy `%LocalAppData%\JoystickInputTuner` data is migrated on first run.

## Development

Canonical source: `C:\Users\at747\source\repos\JoystickInputTuner_Engine\` (channel **DEV**). This mirror is **pre-release** (**PR-R**). Sync via robocopy per project checklist in maintainer docs.
