# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

Pre-release (this repo): **2.1.3 Build PR-R3P1**  
Development (Engine): **2.1.3 Build DEV3P1**

## [2.1.3] — 2026-05-28

Pre-release (GitHub local): **2.1.3 Build PR-R3P1** · Development (Engine): **2.1.3 Build DEV3P1**

### Added

- **Background agent** (`--agent`) — hidden process continues filtering after the main window closes; Windows startup registration unchanged.
- **Handoff** — with stream running and an applied profile, closing the UI saves session/preferences and spawns `exe --agent --delay-ms=1500` to resume vJoy output without reboot.
- **Filter session v3** (`_Data/filters.json`) — persists filters plus device/axis/polling, calibration, monitor axis toggles, and UI preferences (language, sink, vJoy id, logging, auto-apply).
- **Axis bind lock** — lock the stream axis at center via joystick button, keyboard key, or mouse button (toggle on press); **Reset bind**; requires **Start** and lock enabled.
- **Agent window chrome** — agent process starts with no visible main window (no `StartupUri` flash during handoff).
- **Monitor chart overlays** — other device axes as semi-transparent raw lines; per-axis checkboxes (X, Y, Z, RX, RY, RZ, SL0, SL1).
- **Diagnostics — chart axes:** `chartStream`, `chartOverlay`, per-axis `chart_*` in movement/monitor logs; `chart-axes` events.
- **Spike gate RailHold**, **Output settle**, **vJoy busy recovery** (stale handle release).
- **T.A320 Pilot** embedded profile and portable appsettings seed (RU, auto-apply).
- **Ultra-Spike Guard**, **Swing Bypass**, **Axis intent**, **CrossAxisLockSmoother**.
- **Filter session** (v1–v2): debounced save, `FilterSettingsSnapshot`, restore after profile auto-apply.
- **Log reset** on startup and **Clear log** in Settings; **App version** in title and startup log.
- **Z impulse guard**, **cross-axis shield** (hard lock + leak), radial spike zones, dedicated poll thread, monitor history ring buffer.

### Changed

- Default poll rate **175 Hz**; vJoy deadband and synchronous publish on hot path.
- **Hard-lock:** saturated watched axes ignored; RZ↔XY crosstalk fixes; instant snap when leak = 0.
- `FilterSettingsNormalizer`: all standard axes (X, Y, Z, RX, RY, RZ, SL0, SL1); **bind lock binding is not cleared** on sanitize.
- Monitor records overlay history whenever stream runs; legend shows stream axis on Raw/Filtered only.
- Handoff UI hides immediately; shutdown via `Application.Shutdown()` after spawn.
- Release base semver **2.1.3** (from 2.0.0 dev cycle).

### Fixed

- Agent process in Task Manager without active filtering (workflow requires **Apply** + `AgentResumeStream` prefs).
- Main window **re-opening briefly** when handing off to agent (`StartupUri` + late `Hide()`).
- `filters.json` not saving/restoring all tabs; `FilterPipeline.LoadSettings` missing **AxisBindLock**.
- `FilterSettingsNormalizer` trimming watched axes to X/Y only.
- Monitor overlay empty while legend worked; startup timer crash; poll thread reading `PollingSlider`.
- Parasitic yaw when dominance + RZ ≈ XY; vJoy device-in-use without recovery; profile save incomplete fields.

## [1.0.0] — 2026-05-26

Initial public release.

### Added

- Settings checkbox to enable/disable startup agent directly in app.
- "Default settings" button to restore baseline profile/settings behavior.
- Centralized `_Data` folder beside executable for settings, profiles, and logs.
- Automatic migration from legacy `%LocalAppData%\JoystickInputTuner` data.
- Portable publish configuration: self-contained single-file `win-x64` executable.
- Portable build seeds default `_Data` (EN language, default profile/settings).
- UI language persisted in `appsettings.json` (`UiLanguage`).
- **vJoy output sink**: only the selected filtered axis is sent to the virtual device; other vJoy axes remain centered.
