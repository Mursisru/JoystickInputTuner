using JoystickInputTuner.Core.Models;

namespace JoystickInputTuner.Core.Filters;

public static class CrossAxisShieldPolicy
{
    public static bool EvaluateOtherAxesActive(
        CrossAxisShieldSettings shield,
        string targetAxisId,
        string processedAxisId,
        IReadOnlyDictionary<string, double> currentAxes,
        IReadOnlyDictionary<string, double>? previousAxes,
        double deltaTimeSeconds,
        ref bool activityLatched,
        ref int sustainedSampleCount)
    {
        return CrossAxisShieldActivity.Evaluate(
            shield,
            targetAxisId,
            processedAxisId,
            currentAxes,
            previousAxes,
            deltaTimeSeconds,
            ref activityLatched,
            ref sustainedSampleCount);
    }

    /// <summary>0 = no block, 1 = full block toward anchor.</summary>
    public static double ComputeBlockStrength(
        CrossAxisShieldSettings shield,
        bool otherAxesActive,
        bool targetIntentActive,
        double otherAxesPeakDeflection,
        int sustainedSampleCount = 0,
        double targetAxisDeflection = 0.0,
        IReadOnlyDictionary<string, double>? axes = null,
        string targetAxisId = "")
    {
        if (!shield.Enabled)
            return 0.0;

        if (shield.RespectTargetIntent && targetIntentActive)
            return 0.0;

        var threshold = Math.Clamp(shield.OtherAxisDeflectionThreshold, 0.01, 0.95);
        var fullAt = threshold * Math.Clamp(shield.FullBlockDeflectionMultiplier, 1.2, 5.0);
        var engageRatio = shield.HardLockWhenActive
            ? Math.Min(shield.BlockEngageRatio, 0.65)
            : shield.BlockEngageRatio;
        var knee = threshold * Math.Clamp(engageRatio, 0.5, 1.0);

        if (otherAxesPeakDeflection <= knee)
            return 0.0;

        if (!otherAxesActive &&
            axes != null &&
            !string.IsNullOrWhiteSpace(targetAxisId) &&
            !OtherAxisActivityTracker.ShouldBlockParasiticTarget(
                shield, targetAxisId, targetAxisDeflection, axes, otherAxesPeakDeflection))
            return 0.0;

        if (shield.HardLockWhenActive &&
            (otherAxesActive || sustainedSampleCount >= 1) &&
            otherAxesPeakDeflection >= threshold * 0.85)
            return 1.0;

        if (otherAxesPeakDeflection >= fullAt)
            return 1.0;

        var peakBlock = Math.Clamp((otherAxesPeakDeflection - knee) / (fullAt - knee), 0.0, 1.0);

        if (otherAxesActive)
            return peakBlock;

        var minSamples = Math.Clamp(shield.MinOtherAxisActiveSamples, 1, 30);
        if (sustainedSampleCount <= 0)
            return 0.0;

        var preLatch = Math.Clamp((double)sustainedSampleCount / minSamples, 0.0, 1.0);
        var strength = peakBlock * preLatch;
        if (shield.HardLockWhenActive && sustainedSampleCount >= 1)
            strength = Math.Max(strength, peakBlock * 0.98);

        return strength;
    }

    private const double HardLockSnapThreshold = 0.20;
    private const double MicroDriftClampAbs = 0.028;

    /// <summary>Final clamp after smoother — zero leak when hard-lock is engaged.</summary>
    public static double ApplyHardLockOutput(
        CrossAxisShieldSettings shield,
        double blockStrength,
        double value,
        double anchor)
    {
        if (!shield.Enabled || !shield.HardLockWhenActive || blockStrength <= 0.0001)
            return value;

        var leak = Math.Clamp(
            Math.Min(shield.LockLeakMultiplier, shield.ParasiticClampLeakMultiplier),
            0.0,
            1.0);
        var lockValue = anchor + ((value - anchor) * leak);

        if (blockStrength >= HardLockSnapThreshold && leak <= 0.001)
            return lockValue;

        var blended = value + ((lockValue - value) * Math.Clamp(blockStrength, 0.0, 1.0));
        if (blockStrength >= HardLockSnapThreshold && Math.Abs(blended) < MicroDriftClampAbs)
            return anchor;

        return blended;
    }

    public static bool ShouldBlockTarget(double blockStrength) => blockStrength > 0.0001;
}
