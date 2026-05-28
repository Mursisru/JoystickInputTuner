using JoystickInputTuner.Core.Models;

namespace JoystickInputTuner.Core.Filters;

/// <summary>
/// Detects intentional movement on the processed (target) axis vs brief parasitic impulses.
/// </summary>
public sealed class AxisIntentTracker
{
    private int _candidateSign;
    private int _candidateSamples;
    private bool _latched;

    public bool IsIntentional => _latched;

    public bool Update(AxisIntentSettings settings, double axisValue, bool otherAxesActive)
    {
        if (!settings.Enabled)
        {
            ResetState();
            return false;
        }

        var abs = Math.Abs(axisValue);
        var deflection = Math.Clamp(settings.DeflectionThreshold, 0.01, 0.95);
        var release = deflection * Math.Clamp(settings.ReleaseRatio, 0.3, 0.9);
        var strong = Math.Clamp(settings.StrongDeflectionThreshold, deflection, 0.99);
        var confirmSamples = Math.Clamp(settings.ConfirmSamples, 1, 12);
        if (otherAxesActive)
            confirmSamples = Math.Max(confirmSamples, Math.Clamp(settings.ConfirmSamplesWhileOthersActive, 2, 16));

        var allowInstantStrong = !(otherAxesActive && settings.DisableInstantIntentWhileOthersActive);

        if (allowInstantStrong && abs >= strong)
        {
            _latched = true;
            _candidateSign = axisValue >= 0 ? 1 : -1;
            _candidateSamples = confirmSamples;
            return true;
        }

        if (abs < release)
        {
            _candidateSign = 0;
            _candidateSamples = 0;
            _latched = false;
            return false;
        }

        if (abs < deflection)
            return _latched;

        var sign = axisValue >= 0 ? 1 : -1;
        if (sign != _candidateSign)
        {
            _candidateSign = sign;
            _candidateSamples = 1;
            return _latched;
        }

        _candidateSamples++;
        if (_candidateSamples >= confirmSamples)
            _latched = true;

        return _latched;
    }

    public void Reset(double initialValue = 0.0)
    {
        ResetState();
        _ = initialValue;
    }

    private void ResetState()
    {
        _candidateSign = 0;
        _candidateSamples = 0;
        _latched = false;
    }
}
