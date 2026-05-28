using System.Collections.Generic;

namespace JoystickInputTuner.Core.Filters;

public sealed class HampelFilter : IInputFilter
{
    private readonly Queue<double> _window = new();
    private bool _rapidMotionActive;

    public double Process(double value, FilterContext context)
    {
        var settings = context.Settings.Hampel;
        if (!settings.Enabled)
            return value;

        var windowSize = Math.Clamp(settings.WindowSize, 5, 31);
        if (windowSize % 2 == 0)
            windowSize++;

        _window.Enqueue(value);
        while (_window.Count > windowSize)
            _window.Dequeue();

        if (_window.Count < 5)
            return value;

        var samples = _window.ToArray();
        Array.Sort(samples);
        _rapidMotionActive = FilterMotion.UpdateRapidMotionState(_rapidMotionActive, samples);
        if (_rapidMotionActive)
            return value;

        var median = samples[samples.Length / 2];

        var absDev = new double[samples.Length];
        for (var i = 0; i < samples.Length; i++)
            absDev[i] = Math.Abs(samples[i] - median);
        Array.Sort(absDev);

        var mad = Math.Max(1e-6, absDev[absDev.Length / 2]);
        var threshold = settings.SigmaMultiplier * 1.4826 * mad;

        if (Math.Abs(value - median) > threshold)
        {
            context.Statistics.RegisterHampelOutlier();
            return median;
        }

        return value;
    }

    public void Reset(double currentValue)
    {
        _window.Clear();
        _window.Enqueue(currentValue);
        _rapidMotionActive = false;
    }
}
