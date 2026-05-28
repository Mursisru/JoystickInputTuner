namespace JoystickInputTuner.Core.Filters;

public sealed class DeadzoneFilter : IInputFilter
{
    public double Process(double value, FilterContext context)
    {
        var settings = context.Settings.Deadzone;
        if (!settings.Enabled)
            return value;

        var threshold = settings.Radius;
        if (settings.Dynamic)
        {
            // Dynamic deadzone keeps low jitter out but opens up when the stick moves away from center.
            threshold *= Math.Clamp(1.0 - (Math.Abs(value) * settings.DynamicMultiplier), 0.3, 1.0);
        }

        if (Math.Abs(value) <= threshold)
            return 0.0;

        var sign = Math.Sign(value);
        var range = Math.Max(0.0001, 1.0 - threshold);
        var scaled = (Math.Abs(value) - threshold) / range;
        return Math.Clamp(sign * scaled, -1.0, 1.0);
    }

    public void Reset(double currentValue)
    {
    }
}
