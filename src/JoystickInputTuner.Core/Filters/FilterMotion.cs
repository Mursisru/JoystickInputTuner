namespace JoystickInputTuner.Core.Filters;

internal static class FilterMotion
{
    public const double SpreadEnterThreshold = 0.065;
    public const double SpreadExitThreshold = 0.045;

    public static bool UpdateRapidMotionState(bool currentlyActive, double[] sortedSamples)
    {
        if (sortedSamples.Length < 3)
            return false;

        var spread = sortedSamples[^1] - sortedSamples[0];
        if (!currentlyActive && spread > SpreadEnterThreshold)
            return true;

        if (currentlyActive && spread < SpreadExitThreshold)
            return false;

        return currentlyActive;
    }
}
