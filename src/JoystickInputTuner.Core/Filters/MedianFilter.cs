using System.Collections.Generic;

namespace JoystickInputTuner.Core.Filters;

public sealed class MedianFilter : IInputFilter
{
    private readonly Queue<double> _window = new();
    private bool _rapidMotionActive;

    public double Process(double value, FilterContext context)
    {
        var settings = context.Settings.Median;
        if (!settings.Enabled)
            return value;

        var windowSize = Math.Clamp(settings.WindowSize, 3, 25);
        if (windowSize % 2 == 0)
            windowSize++;

        _window.Enqueue(value);
        while (_window.Count > windowSize)
            _window.Dequeue();

        var sorted = _window.ToArray();
        Array.Sort(sorted);
        _rapidMotionActive = FilterMotion.UpdateRapidMotionState(_rapidMotionActive, sorted);
        if (_rapidMotionActive)
            return value;

        return sorted[sorted.Length / 2];
    }

    public void Reset(double currentValue)
    {
        _window.Clear();
        _window.Enqueue(currentValue);
        _rapidMotionActive = false;
    }
}
