namespace JoystickInputTuner.Core.Filters;

public sealed class EmaFilter : IInputFilter
{
    private const double CatchUpErrorScale = 0.15;
    private const double QuietZoneThreshold = 0.10;
    private const double QuietOutputThreshold = 0.12;
    private const double QuietSettleAlpha = 0.30;
    private const double CenterSnapThreshold = 0.035;

    private bool _initialized;
    private double _state;

    public double Process(double value, FilterContext context)
    {
        var settings = context.Settings.Ema;
        if (!settings.Enabled)
        {
            _state = value;
            _initialized = true;
            return value;
        }

        if (!_initialized)
        {
            _state = value;
            _initialized = true;
            return value;
        }

        var alpha = Math.Clamp(settings.Alpha, 0.01, 1.0);
        var signalDistance = FilterRadial.SignalDistance(value, _state);
        var zoneDistance = FilterRadial.ResolveZoneDistance(context.RawInput, signalDistance);
        var nearCenter = zoneDistance < QuietZoneThreshold;

        if (nearCenter && Math.Abs(value) < QuietOutputThreshold)
        {
            if (Math.Abs(context.RawInput) < CenterSnapThreshold && Math.Abs(_state) < QuietOutputThreshold)
            {
                _state = 0;
                return 0;
            }

            _state += QuietSettleAlpha * (value - _state);
            if (Math.Abs(_state) < CenterSnapThreshold && Math.Abs(context.RawInput) < CenterSnapThreshold)
                _state = 0;

            return _state;
        }

        var error = Math.Abs(value - _state);
        var adaptiveAlpha = Math.Min(1.0, alpha + (1.0 - alpha) * Math.Min(1.0, error / CatchUpErrorScale));

        var releaseLag = Math.Abs(_state - context.RawInput);
        if (Math.Abs(context.RawInput) < 0.07 && Math.Abs(_state) > 0.15 && releaseLag > 0.12)
            adaptiveAlpha = Math.Max(adaptiveAlpha, 0.50);

        _state += adaptiveAlpha * (value - _state);
        return _state;
    }

    public void Reset(double currentValue)
    {
        _state = currentValue;
        _initialized = true;
    }
}
