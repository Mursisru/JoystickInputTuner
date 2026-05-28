namespace JoystickInputTuner.Core.Filters;

/// <summary>
/// Snaps residual ripple to zero near center and tracks saturated raw input at rails.
/// </summary>
public sealed class OutputSettleFilter : IInputFilter
{
    private const double CenterRawThreshold = 0.045;
    private const double CenterOutputThreshold = 0.085;
    private const double RailRawThreshold = 0.94;
    private const int CenterQuietSamplesRequired = 2;

    private int _centerQuietSamples;

    public double Process(double value, FilterContext context)
    {
        var raw = context.RawInput;
        var absRaw = Math.Abs(raw);

        if (absRaw >= RailRawThreshold)
        {
            _centerQuietSamples = 0;
            // Track intentional rail deflection; do not override spike/rail-hold suppression.
            if (Math.Abs(value) >= absRaw - 0.08)
                return raw;

            return value;
        }

        if (absRaw < CenterRawThreshold && Math.Abs(value) < CenterOutputThreshold)
        {
            _centerQuietSamples++;
            if (_centerQuietSamples >= CenterQuietSamplesRequired)
                return 0;
        }
        else
        {
            _centerQuietSamples = 0;
        }

        return value;
    }

    public void Reset(double currentValue)
    {
        _centerQuietSamples = Math.Abs(currentValue) < CenterOutputThreshold ? CenterQuietSamplesRequired : 0;
    }
}
