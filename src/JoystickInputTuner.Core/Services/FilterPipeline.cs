using JoystickInputTuner.Core.Filters;
using JoystickInputTuner.Core.Models;

namespace JoystickInputTuner.Core.Services;

public sealed class FilterPipeline
{
    private readonly IInputFilter[] _filters;
    private DateTimeOffset _lastTimestamp;
    private long _sequence;

    public FilterPipeline()
    {
        Statistics = new FilterStatistics();
        _filters =
        [
            new DeadzoneFilter(),
            new MedianFilter(),
            new HampelFilter(),
            new SpikeGateFilter(),
            new ZImpulseGuardFilter(),
            new RateLimiterFilter(),
            new EmaFilter(),
            new OutputSettleFilter(),
        ];
    }

    public FilterSettings Settings { get; } = new();

    public FilterStatistics Statistics { get; }

    public bool TargetAxisIntentActive { get; set; }

    public double CrossAxisBlockStrength { get; set; }

    public OutputSample Process(double normalizedInput)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var delta = _lastTimestamp == default
            ? TimeSpan.FromMilliseconds(4)
            : timestamp - _lastTimestamp;
        if (delta <= TimeSpan.Zero)
            delta = TimeSpan.FromMilliseconds(1);

        var context = new FilterContext
        {
            Settings = Settings,
            Statistics = Statistics,
            DeltaTime = delta,
            RawInput = normalizedInput,
            TargetAxisIntentActive = TargetAxisIntentActive,
            CrossAxisBlockStrength = CrossAxisBlockStrength
        };

        var value = Math.Clamp(normalizedInput, -1.0, 1.0);
        var beforeSpikeCount = Statistics.SpikeSuppressedCount;
        foreach (var filter in _filters)
            value = Math.Clamp(filter.Process(value, context), -1.0, 1.0);

        _lastTimestamp = timestamp;
        _sequence++;

        return new OutputSample(
            RawValue: normalizedInput,
            FilteredValue: value,
            Timestamp: timestamp,
            Sequence: _sequence,
            SpikeSuppressed: Statistics.SpikeSuppressedCount > beforeSpikeCount);
    }

    public void Reset(double initialValue = 0.0)
    {
        _lastTimestamp = default;
        _sequence = 0;
        Statistics.Reset();
        foreach (var filter in _filters)
            filter.Reset(initialValue);
    }

    public void LoadSettings(FilterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var clone = FilterSettingsSnapshot.Clone(settings);
        Settings.Deadzone = clone.Deadzone;
        Settings.Median = clone.Median;
        Settings.Hampel = clone.Hampel;
        Settings.SpikeGate = clone.SpikeGate;
        Settings.ZImpulseGuard = clone.ZImpulseGuard;
        Settings.AxisIntent = clone.AxisIntent;
        Settings.CrossAxisShield = clone.CrossAxisShield;
        Settings.AxisBindLock = clone.AxisBindLock;
        Settings.RateLimiter = clone.RateLimiter;
        Settings.Ema = clone.Ema;
    }
}
