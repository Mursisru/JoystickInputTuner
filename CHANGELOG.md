# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

Pre-release (this repo): **1.0.0 Build PR-R2P34**  
Development (Engine): **1.0.0 Build DEV2P34**

### Added

- **Monitor chart overlays:** all other joystick axes as semi-transparent raw lines on the same graph as the stream axis.
- **Monitor axis toggles:** checkboxes (X, Y, Z, RX, RY, RZ, SL0, SL1) to enable/disable each overlay; stream axis shown on Raw/Filtered only.
- **Diagnostics — chart axes:** `chartStream`, `chartOverlay`, per-axis `chart_*` values in `movement` / `monitor` logs; `chart-axes` events on toggle, stream start, and axis change.
- **Spike gate RailHold** — hold output when raw flips sign near rail (electrical noise on saturated axes).
- **Output settle** filter and tighter vJoy publish deadband for idle stability.
- **vJoy busy recovery** — stale handle release and clearer status when device #1 is in use.
- **T.A320 Pilot** embedded default profile (`ta320-pilot-profile.json`) and portable appsettings seed (RU, auto-apply).
- **Filter session** (`_Data/filters.json`): debounced save, restore after profile auto-apply, force save on Stop / close / Save profile / Apply.
- `FilterSettingsSnapshot` deep clone; `ApplyFilterSettings` / `CaptureFilterSettingsFromUi` for full UI ↔ pipeline sync.
- **Ultra-Spike Guard** (center) and **Swing Bypass** (pass large deliberate stick throws with relaxed thresholds).
- **Axis intent** tracking and **CrossAxisLockSmoother** for gradual hard-lock transitions.
- **Diagnostics log**: natural/filtered values, block strength, cross-axis peaks, full settings snapshot (`FilterSettingsLogFormatter`).
- **Log reset**: optional clear on startup (`ResetLogOnStartup`) and **Clear log** button in Settings.
- **App version** in code (`AppVersion.cs`), window title, UI subtitle, and startup log.
- **Radial spike zones**, center smoothing multiplier, fine delta sliders for spike gate.
- Dedicated **DirectInput poll thread** and single-axis read path.
- **History ring buffer** for monitor graph (records all axes while stream is running).
- **Monitor-only mode** (no vJoy while tuning).
- **Z impulse protection** and **cross-axis shield** (variable block 0..1) with **hard lock** + leak multiplier.
- `tools/VJoyDiag` smoke-test; optional `lib/vJoy/x64/vJoyInterface.dll`.

### Changed

- Default poll rate **175 Hz**; vJoy deadband + synchronous `Publish()` on hot path.
- **Monitor:** overlay history recorded whenever stream is running (not only when Monitor tab is open); legend shows stream axis on Raw/Filtered lines only.
- **Hard-lock:** ignore saturated watched axes (±1); correlated RZ↔XY crosstalk block; skip dominance when XY latched; instant snap when leak = 0.
- `FilterSettingsNormalizer`: watched axes **X,Y** only; zero cross leak when hard lock enabled.
- **EMA / Rate limiter:** adaptive smoothing; motion bypass for median/hampel; center snap tweaks.
- **Spike gate:** quiet-zone gating, motion hysteresis, radial zone blend (`FilterRadial`).
- Filter session save **suppressed until `OnLoaded`** completes (prevents overwriting `filters.json` with defaults).
- T.A320 Pilot profile: outer spike / rail-hold tuning for yaw stability.

### Fixed

- Monitor overlay axes empty while legend worked (history only recorded when Monitor tab was bound).
- Startup crash when filter save timer ran before `InitializeComponent()`.
- Monitor graph / status read `PollingSlider` from poll thread (now cached at Start).
- `filters.json` not persisting or restoring correctly (timer race, incomplete snapshot, missing `LoadSettings` on restore).
- Profile **Save** wrote incomplete filter fields (hidden shield / ultra-spike settings).
- Parasitic yaw leak when `RequireOtherAxisDominance` and RZ ≈ XY peak (crosstalk); log showed `block=0` with active XY.
- Filter lag / center ringing / spike delta slider stuck at extremes (earlier iterations).
- vJoy “device in use” without recovery path; graph vibration at idle (settle + rail hold + profile).

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
