namespace JoystickInputTuner.Core.Models;

public sealed class FilterStatistics
{
    public int SpikeSuppressedCount { get; private set; }

    public int HampelOutlierCount { get; private set; }

    public void RegisterSpikeSuppressed() => SpikeSuppressedCount++;

    public void RegisterHampelOutlier() => HampelOutlierCount++;

    public void Reset()
    {
        SpikeSuppressedCount = 0;
        HampelOutlierCount = 0;
    }
}
