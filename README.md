**Developer:** Mursisru

# Joystick Input Tuner

[![.NET Framework](https://img.shields.io/badge/Platform-.NET%20Framework%204.8-512BD4)](https://dotnet.microsoft.com/download/dotnet-framework/net48) [![Version](https://img.shields.io/badge/Version-2.1.3-green)](https://github.com/Mursisru/JoystickInputTuner/releases/tag/v2.1.3)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow)](https://github.com/Mursisru/JoystickInputTuner/blob/main/LICENSE)


Desktop WPF tool for joystick input diagnostics and filtering (focus on yaw-axis jitter/spikes).

**Current dev build:** `2.1.3 Build DEV3P1` (shown in app title and main window).

**Target release:** `2.1.3`

---

## Critical warnings

> [!IMPORTANT]
> **vJoy required for game output** - install [vJoy](https://sourceforge.net/projects/vjoy/) and enable **Device #1**; bind the **game** to the vJoy axis, not the physical stick.

> [!WARNING]
> **Do not bind the physical stick in-game** - if the game reads your real device instead of vJoy, all filtering is bypassed.

> [!IMPORTANT]
> **Apply + Start before closing the window** - closing while streaming hands off to `--agent` background process.

> [!TIP]
> **Physical stick -> app -> vJoy -> game** - the game must read the virtual device for the filtered axis.

> [!NOTE]
> **Data lives in `_Data/` next to the exe** - profiles, filters, logs; legacy `%LocalAppData%` migrates on first run.

## Features

- Device and axis selection (including dynamic axis detection).
- Real-time **Monitor**: raw vs filtered for the stream axis; **overlay** other device axes (semi-transparent lines).
- **Per-axis toggles** on Monitor to show/hide overlay lines (X, Y, Z, RX, RY, RZ, SL0, SL1).
- Filter pipeline: Deadzone, Median, Hampel, Spike Gate (radial zones, **RailHold**, **Ultra-Spike**, **Swing Bypass**), **Output Settle**, Z Impulse Guard, Cross-Axis Shield (hard lock + intent), Rate Limiter, EMA.
- **Axis bind lock** — center the stream axis via joystick / keyboard / mouse binding (toggle on press).
- **Filter session v3** — `_Data/filters.json` saves filters, device/axis/polling, calibration, monitor toggles, and UI preferences (debounced; restored on launch).
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

The physical device is read by this app; the game should use the vJoy device for the filtered axis.

## Handoff to background agent

1. **Apply** a profile and **Start** the stream.
2. Close the main window — the app saves state and starts a hidden `--agent` process (~1.5 s delay) to keep filtering.
3. Check `{exeDir}\_Data\logs\tuner_YYYYMMDD.log` for `agent-handoff` and `stream-start`.

## Data Storage

Application data is stored near the executable in:

- `_Data/appsettings.json` — UI language, logging, auto-apply, agent resume flags, **ResetLogOnStartup**
- `_Data/profiles/*.json` — named profiles (Save profile)
- `_Data/filters.json` — filter session v3 (filters + input/monitor/UI state)
- `_Data/logs/tuner_YYYYMMDD.log` — movement, monitor, settings, chart axis selection

If legacy data exists in `%LocalAppData%\JoystickInputTuner`, it is migrated automatically on first run.

## Build

```powershell
dotnet build .\src\JoystickInputTuner.App\JoystickInputTuner.App.csproj -c Release
```

Requires Windows and .NET 10 SDK.

## Portable (local)

Every **Release** build copies a self-contained single-file app to:

- `Portable/JoystickInputTuner.App.exe` (~140 MB; **not** committed to GitHub — build locally)
- `Portable/vJoyInterface.dll`
- `Portable/_Data/` — fresh defaults each build (T.A320 profile seed, EN UI). Runtime changes persist in `_Data` beside the exe.

Run: `Portable\JoystickInputTuner.App.exe` (keep `vJoyInterface.dll` in the same folder).

---

## Keywords

windows, wpf, dotnet-framework, joystickinputtuner, csharp
