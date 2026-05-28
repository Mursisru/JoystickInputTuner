using JoystickInputTuner.Core.Models;

namespace JoystickInputTuner.Core.Filters;

public static class CrossAxisShieldActivity
{
    public static bool Evaluate(
        CrossAxisShieldSettings shield,
        string targetAxisId,
        string currentProcessedAxisId,
        IReadOnlyDictionary<string, double> currentAxes,
        IReadOnlyDictionary<string, double>? previousAxes,
        double deltaTimeSeconds,
        ref bool latched,
        ref int sustainedSampleCount)
    {
        if (!shield.Enabled)
        {
            latched = false;
            sustainedSampleCount = 0;
            return false;
        }

        if (!targetAxisId.Equals(currentProcessedAxisId, StringComparison.OrdinalIgnoreCase))
            return false;

        var watchedAxes = shield.WatchedAxes ?? [];
        if (watchedAxes.Length == 0)
        {
            latched = false;
            sustainedSampleCount = 0;
            return false;
        }

        var deflectionThreshold = Math.Clamp(shield.OtherAxisDeflectionThreshold, 0.01, 0.95);
        var releaseThreshold = deflectionThreshold * Math.Clamp(shield.ReleaseThresholdRatio, 0.3, 0.8);
        var minVelocity = Math.Max(0.0, shield.MinOtherAxisVelocityPerSecond);
        var minSamples = Math.Clamp(shield.MinOtherAxisActiveSamples, 1, 30);
        if (shield.HardLockWhenActive)
            minSamples = 1;

        var momentaryActive = false;
        var allBelowRelease = true;
        var peakDeflection = 0.0;

        foreach (var watchedAxis in watchedAxes)
        {
            if (watchedAxis.Equals(targetAxisId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!currentAxes.TryGetValue(watchedAxis, out var nowValue))
                continue;

            if (OtherAxisActivityTracker.IsSaturated(nowValue))
                continue;

            var abs = Math.Abs(nowValue);
            peakDeflection = Math.Max(peakDeflection, abs);

            if (abs >= deflectionThreshold)
                momentaryActive = true;

            if (abs >= releaseThreshold)
                allBelowRelease = false;

            if (previousAxes != null &&
                previousAxes.TryGetValue(watchedAxis, out var prevValue) &&
                deltaTimeSeconds > 0.0001)
            {
                var velocity = Math.Abs(nowValue - prevValue) / deltaTimeSeconds;
                if (abs >= releaseThreshold && velocity >= minVelocity)
                    momentaryActive = true;
            }
        }

        if (momentaryActive)
            sustainedSampleCount++;
        else if (allBelowRelease)
            sustainedSampleCount = 0;

        if (sustainedSampleCount >= minSamples)
            latched = true;
        else if (allBelowRelease)
            latched = false;

        return latched;
    }

    public static double GetPeakDeflection(
        CrossAxisShieldSettings shield,
        string targetAxisId,
        IReadOnlyDictionary<string, double> axes)
    {
        return OtherAxisActivityTracker.GetPeakDeflection(shield, targetAxisId, axes);
    }
}
