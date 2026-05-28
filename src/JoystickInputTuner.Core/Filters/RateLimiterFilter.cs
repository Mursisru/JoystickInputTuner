namespace JoystickInputTuner.Core.Filters;

public sealed class RateLimiterFilter : IInputFilter
{
    private const double LagCatchUpThreshold = 0.08;
    private const double MaxCatchUpMultiplier = 2.5;
    private const double QuietRawThreshold = 0.10;
    private const double QuietOutputMargin = 0.05;
    private const double CenterSnapRawThreshold = 0.05;
    private const double CenterSnapOutputThreshold = 0.10;
    private const double RailSnapRawThreshold = 0.94;

    private double _lastValue;

    public double Process(double value, FilterContext context)
    {
        var settings = context.Settings.RateLimiter;
        if (!settings.Enabled)
        {
            _lastValue = value;
            return value;
        }

        var raw = context.RawInput;
        if (Math.Abs(raw) < CenterSnapRawThreshold && Math.Abs(value) < CenterSnapOutputThreshold)
        {
            _lastValue = 0;
            return 0;
        }

        if (Math.Abs(raw) >= RailSnapRawThreshold && Math.Abs(value) >= RailSnapRawThreshold)
        {
            _lastValue = raw;
            return raw;
        }

        var dt = Math.Max(1e-6, context.DeltaTime.TotalSeconds);
        var maxDelta = settings.MaxDeltaPerSecond * dt;
        var delta = value - _lastValue;
        if (Math.Abs(delta) > maxDelta)
        {
            var blockStrength = Math.Clamp(context.CrossAxisBlockStrength, 0.0, 1.0);
            var lagFromRaw = Math.Abs(_lastValue - context.RawInput);
            var rawNearCenter = Math.Abs(context.RawInput) < QuietRawThreshold;
            var outputStillOutside = Math.Abs(_lastValue) > QuietRawThreshold + QuietOutputMargin;
            var swingZone = Math.Abs(context.RawInput) >= 0.32;
            var allowCatchUp = blockStrength <= 0.01 && (!rawNearCenter || outputStillOutside);

            if (allowCatchUp && lagFromRaw > LagCatchUpThreshold)
            {
                var scale = Math.Min(MaxCatchUpMultiplier, 1.0 + lagFromRaw / 0.15);
                if (swingZone)
                    scale = Math.Min(MaxCatchUpMultiplier * 1.6, scale * 1.75);
                maxDelta *= scale;
            }

            value = _lastValue + Math.Sign(delta) * maxDelta;
        }

        _lastValue = value;
        return value;
    }

    public void Reset(double currentValue)
    {
        _lastValue = currentValue;
    }
}
