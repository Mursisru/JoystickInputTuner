using JoystickInputTuner.Core.Models;

namespace JoystickInputTuner.Core.Filters;

public static class OtherAxisActivityTracker
{
    /// <summary>Axes stuck at hardware limits are ignored (common on RX/RY when not present).</summary>
    public const double SaturatedAxisAbs = 0.98;

    public static bool IsSaturated(double value) => Math.Abs(value) >= SaturatedAxisAbs;

    public static double GetPeakDeflection(
        CrossAxisShieldSettings shield,
        string targetAxisId,
        IReadOnlyDictionary<string, double> axes)
    {
        var peak = 0.0;
        foreach (var watchedAxis in shield.WatchedAxes ?? [])
        {
            if (watchedAxis.Equals(targetAxisId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (axes.TryGetValue(watchedAxis, out var value) && !IsSaturated(value))
                peak = Math.Max(peak, Math.Abs(value));
        }

        return peak;
    }

    public static bool ShouldBlockParasiticTarget(
        CrossAxisShieldSettings shield,
        string targetAxisId,
        double targetAxisValue,
        IReadOnlyDictionary<string, double> axes,
        double otherAxesPeakDeflection = double.NaN)
    {
        if (!shield.RequireOtherAxisDominance)
            return true;

        var peak = double.IsNaN(otherAxesPeakDeflection)
            ? GetPeakDeflection(shield, targetAxisId, axes)
            : otherAxesPeakDeflection;
        var deflectionThreshold = Math.Clamp(shield.OtherAxisDeflectionThreshold, 0.01, 0.95);
        if (peak < deflectionThreshold)
            return false;

        var targetAbs = Math.Abs(targetAxisValue);
        if (targetAbs < deflectionThreshold)
            return true;

        // Hardware crosstalk often kicks RZ to the same magnitude as X/Y in one poll.
        var trackingBand = Math.Max(0.045, targetAbs * 0.20);
        if (Math.Abs(peak - targetAbs) <= trackingBand)
            return true;

        var ratio = Math.Clamp(shield.OtherAxisDominanceRatio, 0.5, 5.0);
        return peak >= targetAbs * ratio;
    }

    public static bool OthersDominateTarget(
        CrossAxisShieldSettings shield,
        string targetAxisId,
        double targetAxisValue,
        IReadOnlyDictionary<string, double> axes)
        => ShouldBlockParasiticTarget(shield, targetAxisId, targetAxisValue, axes);
}
