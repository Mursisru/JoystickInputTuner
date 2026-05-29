namespace JoystickInputTuner.Core.Models;

public sealed class FilterSettings
{
    public DeadzoneSettings Deadzone { get; set; } = new();

    public MedianSettings Median { get; set; } = new();

    public HampelSettings Hampel { get; set; } = new();

    public SpikeGateSettings SpikeGate { get; set; } = new();

    public ZImpulseGuardSettings ZImpulseGuard { get; set; } = new();

    public AxisIntentSettings AxisIntent { get; set; } = new();

    public CrossAxisShieldSettings CrossAxisShield { get; set; } = new();

    /// <summary>Full lock of the stream axis while a joystick button is held (or toggled).</summary>
    public AxisBindLockSettings AxisBindLock { get; set; } = new();

    public RateLimiterSettings RateLimiter { get; set; } = new();

    public EmaSettings Ema { get; set; } = new();
}

public sealed class AxisIntentSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>|value| above this starts intent confirmation.</summary>
    public double DeflectionThreshold { get; set; } = 0.07;

    /// <summary>Release latch when |value| drops below DeflectionThreshold * ReleaseRatio.</summary>
    public double ReleaseRatio { get; set; } = 0.55;

    /// <summary>|value| above this counts as intentional immediately.</summary>
    public double StrongDeflectionThreshold { get; set; } = 0.35;

    public int ConfirmSamples { get; set; } = 2;

    public int ConfirmSamplesWhileOthersActive { get; set; } = 6;

    public bool DisableInstantIntentWhileOthersActive { get; set; } = true;
}

public sealed class AxisBindLockSettings
{
    public bool Enabled { get; set; }

    /// <summary>Joystick, Keyboard, or Mouse.</summary>
    public string BindDeviceKind { get; set; } = string.Empty;

    /// <summary>Device id (joystick GUID, or SYSTEM-KEYBOARD / SYSTEM-MOUSE).</summary>
    public string BindDeviceId { get; set; } = string.Empty;

    /// <summary>Joystick / mouse button index (0-based). -1 for keyboard.</summary>
    public int ButtonIndex { get; set; } = -1;

    /// <summary>Keyboard key code (SharpDX Key enum). -1 for joystick/mouse.</summary>
    public int KeyCode { get; set; } = -1;

    /// <summary>Legacy JSON; always toggle. Ignored at runtime.</summary>
    public bool ToggleMode { get; set; } = true;

    /// <summary>Filtered output while locked (usually 0 = center).</summary>
    public double LockAnchor { get; set; } = 0.0;
}

public sealed class CrossAxisShieldSettings
{
    public bool Enabled { get; set; } = false;

    /// <summary>"SELECTED" means currently selected input axis in UI.</summary>
    public string TargetAxisId { get; set; } = "SELECTED";

    public string[] WatchedAxes { get; set; } = ["X", "Y"];

    /// <summary>|watched axis| above this counts as activity (stick deflection).</summary>
    public double OtherAxisDeflectionThreshold { get; set; } = 0.10;

    /// <summary>Consecutive samples above threshold before block engages.</summary>
    public int MinOtherAxisActiveSamples { get; set; } = 4;

    /// <summary>Release latch when |watched| &lt; threshold * ratio.</summary>
    public double ReleaseThresholdRatio { get; set; } = 0.45;

    /// <summary>Block starts ramping above threshold * ratio (soft knee).</summary>
    public double BlockEngageRatio { get; set; } = 0.85;

    /// <summary>Full block at threshold * multiplier.</summary>
    public double FullBlockDeflectionMultiplier { get; set; } = 2.2;

    public double LockSmoothingSeconds { get; set; } = 0.055;

    public double ReleaseSmoothingSeconds { get; set; } = 0.040;

    /// <summary>Block only when watched-axis peak dominates target deflection.</summary>
    public bool RequireOtherAxisDominance { get; set; } = false;

    public double OtherAxisDominanceRatio { get; set; } = 1.15;

    /// <summary>Do not block while sustained intentional movement is detected on target.</summary>
    public bool RespectTargetIntent { get; set; } = true;

    /// <summary>0 = hard clamp to anchor, 1 = no clamp.</summary>
    public double ParasiticClampLeakMultiplier { get; set; } = 0.0;

    public double MinOtherAxisVelocityPerSecond { get; set; } = 0.25;

    public double MaxOtherAxisVelocityPerSecond { get; set; } = 30.0;

    /// <summary>Multiplier for RateLimiter.MaxDeltaPerSecond while shield is active.</summary>
    public double RateLimitMultiplierWhenActive { get; set; } = 0.55;

    /// <summary>Multiplier for EMA alpha while shield is active (less alpha = stronger smoothing).</summary>
    public double EmaAlphaMultiplierWhenActive { get; set; } = 0.60;

    /// <summary>When active, lock target axis around anchor value.</summary>
    public bool HardLockWhenActive { get; set; } = false;

    /// <summary>If enabled, hard-lock anchor is always forced to 0.0 (center).</summary>
    public bool HardLockForceCenter { get; set; } = true;

    /// <summary>0.0 = full lock, 1.0 = no lock effect.</summary>
    public double LockLeakMultiplier { get; set; } = 0.0;
}

public sealed class ZImpulseGuardSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Guard works only near center (|value| <= radius), where hardware micro-impulses are most common.
    /// </summary>
    public double CenterRadius { get; set; } = 0.35;

    /// <summary>
    /// Minimum absolute movement that starts intent detection.
    /// </summary>
    public double IntentThreshold { get; set; } = 0.06;

    /// <summary>
    /// Required consecutive samples with same direction to accept movement.
    /// </summary>
    public int ConfirmSamples { get; set; } = 2;
}

public sealed class DeadzoneSettings
{
    public bool Enabled { get; set; } = true;

    public bool Dynamic { get; set; } = false;

    public double Radius { get; set; } = 0.03;

    public double DynamicMultiplier { get; set; } = 0.6;
}

public sealed class MedianSettings
{
    public bool Enabled { get; set; } = true;

    public int WindowSize { get; set; } = 5;
}

public sealed class HampelSettings
{
    public bool Enabled { get; set; } = true;

    public int WindowSize { get; set; } = 9;

    public double SigmaMultiplier { get; set; } = 3.0;
}

public sealed class SpikeGateSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Distance-aware spike rules: aggressive near center, strict at outer deflection.</summary>
    public bool RadialZonesEnabled { get; set; } = true;

    /// <summary>Flat-mode thresholds (also used as outer zone when radial is enabled).</summary>
    public double DeltaThreshold { get; set; } = 0.38;

    public double VelocityThresholdPerSecond { get; set; } = 11.0;

    public int MaxConsecutiveSuppressions { get; set; } = 1;

    /// <summary>|raw| below this uses center spike rules.</summary>
    public double CenterZoneEnd { get; set; } = 0.28;

    public double CenterDeltaThreshold { get; set; } = 0.05;

    /// <summary>1.0 = default; higher = stronger center spike smoothing.</summary>
    public double CenterSmoothingMultiplier { get; set; } = 1.0;

    public double CenterVelocityThresholdPerSecond { get; set; } = 1.8;

    public int CenterMaxConsecutiveSuppressions { get; set; } = 5;

    public double OuterDeltaThreshold { get; set; } = 0.38;

    public double OuterVelocityThresholdPerSecond { get; set; } = 11.0;

    public int OuterMaxConsecutiveSuppressions { get; set; } = 1;

    public double ZoneBlendWidth { get; set; } = 0.15;

    /// <summary>Extra guard for ultra-sharp micro-spikes near center (hardware crosstalk).</summary>
    public bool UltraSpikeGuardEnabled { get; set; } = true;

    /// <summary>|raw| below this uses ultra-spike rules.</summary>
    public double UltraSpikeCenterMaxAbs { get; set; } = 0.22;

    public double UltraSpikeDeltaThreshold { get; set; } = 0.018;

    public double UltraSpikeVelocityThresholdPerSecond { get; set; } = 2.8;

    public int UltraSpikeMaxConsecutiveSuppressions { get; set; } = 10;

    /// <summary>Above this |raw| zone, spike gate uses permissive swing thresholds.</summary>
    public bool SwingBypassEnabled { get; set; } = true;

    public double SwingBypassMinAbs { get; set; } = 0.32;

    public double SwingBypassDeltaThreshold { get; set; } = 0.52;

    public double SwingBypassVelocityThresholdPerSecond { get; set; } = 18.0;

    public int SwingBypassMaxConsecutiveSuppressions { get; set; } = 1;

    /// <summary>
    /// Holds output when raw input flips sign near ±1 (electrical noise / bad axis wiring).
    /// </summary>
    public bool RailHoldEnabled { get; set; } = true;

    public double RailThreshold { get; set; } = 0.90;

    /// <summary>Minimum |raw - previous raw| to treat as a rail flip.</summary>
    public double RailFlipMinDelta { get; set; } = 0.85;
}

public sealed class RateLimiterSettings
{
    public bool Enabled { get; set; } = true;

    public double MaxDeltaPerSecond { get; set; } = 8.0;
}

public sealed class EmaSettings
{
    public bool Enabled { get; set; } = true;

    public double Alpha { get; set; } = 0.40;
}
