namespace JoystickInputTuner.Core.Filters;

internal static class FilterRadial
{
    private const double ReturnHysteresis = 0.03;

    public static double SmoothStep(double edge0, double edge1, double x)
    {
        if (edge0 >= edge1)
            return x >= edge1 ? 1.0 : 0.0;

        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    public static double Lerp(double a, double b, double t) => a + (b - a) * t;

    public static double SignalDistance(params double[] samples)
    {
        var distance = 0.0;
        foreach (var sample in samples)
            distance = Math.Max(distance, Math.Abs(sample));

        return distance;
    }

    public static bool IsReturningToCenter(double rawInput, double signalDistance)
    {
        return Math.Abs(rawInput) + ReturnHysteresis < signalDistance;
    }

    /// <summary>
    /// Raw defines the zone unless the stick is returning and filtered output still lags behind.
    /// </summary>
    public static double ResolveZoneDistance(double rawInput, double signalDistance)
    {
        var rawDistance = Math.Abs(rawInput);
        return IsReturningToCenter(rawInput, signalDistance)
            ? Math.Max(rawDistance, signalDistance)
            : rawDistance;
    }

    /// <summary>0 = center rules, 1 = outer rules. Blend ramps up before center zone ends.</summary>
    public static double CenterToOuterBlend(double zoneDistance, double centerEnd, double blendWidth)
    {
        centerEnd = Math.Clamp(centerEnd, 0.08, 0.70);
        blendWidth = Math.Max(0.01, blendWidth);
        var blendStart = Math.Max(0.0, centerEnd - blendWidth);
        return SmoothStep(blendStart, centerEnd, zoneDistance);
    }
}
