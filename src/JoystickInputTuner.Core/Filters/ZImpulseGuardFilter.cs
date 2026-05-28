using JoystickInputTuner.Core.Models;

namespace JoystickInputTuner.Core.Filters;

/// <summary>
/// Rejects short "false intent" impulses around center.
/// Helps when hardware briefly kicks Z while pilot moves other stick axes.
/// </summary>
public sealed class ZImpulseGuardFilter : IInputFilter
{
    private double _lastAccepted;
    private int _candidateSign;
    private int _candidateSamples;

    public double Process(double value, FilterContext context)
    {
        var s = context.Settings.ZImpulseGuard;
        if (!s.Enabled || context.TargetAxisIntentActive)
            return value;

        var centerRadius = Math.Clamp(s.CenterRadius, 0.05, 0.8);
        var intentThreshold = Math.Clamp(s.IntentThreshold, 0.01, centerRadius);
        var confirmSamples = Math.Clamp(s.ConfirmSamples, 1, 6);
        if (context.CrossAxisBlockStrength > 0.15)
        {
            intentThreshold = Math.Min(intentThreshold, 0.04);
            confirmSamples = Math.Max(confirmSamples, 3);
        }
        var abs = Math.Abs(value);

        if (abs < intentThreshold)
        {
            _candidateSign = 0;
            _candidateSamples = 0;
            _lastAccepted = value;
            return value;
        }

        // Outside center zone we should not add latency to intentional travel.
        if (abs > centerRadius)
        {
            _candidateSign = 0;
            _candidateSamples = 0;
            _lastAccepted = value;
            return value;
        }

        var sign = value >= 0 ? 1 : -1;
        if (sign != _candidateSign)
        {
            _candidateSign = sign;
            _candidateSamples = 1;
            return _lastAccepted;
        }

        _candidateSamples++;
        if (_candidateSamples < confirmSamples)
            return _lastAccepted;

        _lastAccepted = value;
        return value;
    }

    public void Reset(double initialValue)
    {
        _lastAccepted = initialValue;
        _candidateSign = 0;
        _candidateSamples = 0;
    }
}
