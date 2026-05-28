using JoystickInputTuner.Core.Models;

namespace JoystickInputTuner.Core.Filters;

/// <summary>
/// Smoothly pulls target-axis output toward lock anchor (avoids step edges from hard on/off).
/// </summary>
public sealed class CrossAxisLockSmoother
{
    private double _output;
    private bool _initialized;

    public double Output => _output;

    public double Process(
        CrossAxisShieldSettings shield,
        double blockStrength,
        double pipelineFiltered,
        double anchor,
        double deltaTimeSeconds)
    {
        if (!shield.HardLockWhenActive || blockStrength <= 0.0001)
        {
            var passTau = Math.Clamp(shield.ReleaseSmoothingSeconds, 0.01, 0.5);
            _output = SmoothToward(pipelineFiltered, passTau, deltaTimeSeconds);
            return _output;
        }

        var leak = Math.Clamp(
            Math.Min(shield.LockLeakMultiplier, shield.ParasiticClampLeakMultiplier),
            0.0,
            1.0);
        var lockTarget = anchor + ((pipelineFiltered - anchor) * leak);
        if (shield.HardLockWhenActive && blockStrength >= 0.20 && leak <= 0.001)
        {
            _output = lockTarget;
            _initialized = true;
            return _output;
        }

        if (blockStrength >= 0.95 && leak <= 0.001)
        {
            _output = lockTarget;
            _initialized = true;
            return _output;
        }

        var engageTau = Math.Clamp(shield.LockSmoothingSeconds, 0.01, 0.5);
        var blendedTarget = Blend(pipelineFiltered, lockTarget, blockStrength);
        _output = SmoothToward(blendedTarget, engageTau, deltaTimeSeconds);
        return _output;
    }

    public void Reset(double initialValue = 0.0)
    {
        _output = initialValue;
        _initialized = true;
    }

    private double SmoothToward(double target, double tauSeconds, double deltaTimeSeconds)
    {
        if (!_initialized)
        {
            _output = target;
            _initialized = true;
            return _output;
        }

        var dt = Math.Max(1e-6, deltaTimeSeconds);
        var tau = Math.Max(1e-4, tauSeconds);
        var alpha = 1.0 - Math.Exp(-dt / tau);
        _output += (target - _output) * Math.Clamp(alpha, 0.0, 1.0);
        return _output;
    }

    private static double Blend(double a, double b, double t) => a + ((b - a) * Math.Clamp(t, 0.0, 1.0));
}
