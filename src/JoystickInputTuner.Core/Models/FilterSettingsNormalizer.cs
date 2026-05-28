namespace JoystickInputTuner.Core.Models;

public static class FilterSettingsNormalizer
{
    private static readonly HashSet<string> LinearWatchAxes = new(StringComparer.OrdinalIgnoreCase)
    {
        "X", "Y"
    };

    public static FilterSettings Ensure(FilterSettings? settings)
    {
        settings ??= new FilterSettings();
        settings.Deadzone ??= new DeadzoneSettings();
        settings.Median ??= new MedianSettings();
        settings.Hampel ??= new HampelSettings();
        settings.SpikeGate ??= new SpikeGateSettings();
        settings.ZImpulseGuard ??= new ZImpulseGuardSettings();
        settings.AxisIntent ??= new AxisIntentSettings();
        settings.CrossAxisShield ??= new CrossAxisShieldSettings();
        settings.RateLimiter ??= new RateLimiterSettings();
        settings.Ema ??= new EmaSettings();

        SanitizeCrossAxisShield(settings.CrossAxisShield);
        SanitizeSpikeGate(settings.SpikeGate);
        return settings;
    }

    private static void SanitizeCrossAxisShield(CrossAxisShieldSettings shield)
    {
        shield.WatchedAxes ??= ["X", "Y"];
        shield.WatchedAxes = shield.WatchedAxes
            .Where(axis => LinearWatchAxes.Contains(axis))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (shield.WatchedAxes.Length == 0)
            shield.WatchedAxes = ["X", "Y"];

        if (shield.HardLockWhenActive)
        {
            shield.LockLeakMultiplier = 0.0;
            shield.ParasiticClampLeakMultiplier = 0.0;
            shield.MinOtherAxisActiveSamples = Math.Min(shield.MinOtherAxisActiveSamples, 1);
            shield.BlockEngageRatio = Math.Min(shield.BlockEngageRatio, 0.72);
            shield.LockSmoothingSeconds = Math.Min(shield.LockSmoothingSeconds, 0.02);
        }
    }

    private static void SanitizeSpikeGate(SpikeGateSettings spike)
    {
        if (!spike.UltraSpikeGuardEnabled)
            return;

        spike.UltraSpikeDeltaThreshold = Math.Min(spike.UltraSpikeDeltaThreshold, 0.014);
        spike.UltraSpikeVelocityThresholdPerSecond = Math.Min(
            spike.UltraSpikeVelocityThresholdPerSecond,
            2.4);
        spike.UltraSpikeMaxConsecutiveSuppressions = Math.Max(
            spike.UltraSpikeMaxConsecutiveSuppressions,
            14);
    }
}
