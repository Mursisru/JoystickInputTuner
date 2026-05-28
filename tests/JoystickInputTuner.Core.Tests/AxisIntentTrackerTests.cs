using JoystickInputTuner.Core.Filters;
using JoystickInputTuner.Core.Models;

namespace JoystickInputTuner.Core.Tests;

public class AxisIntentTrackerTests
{
    [Fact]
    public void ConfirmsIntent_AfterConfirmSamples()
    {
        var tracker = new AxisIntentTracker();
        var settings = new AxisIntentSettings
        {
            Enabled = true,
            DeflectionThreshold = 0.07,
            ConfirmSamples = 2
        };

        Assert.False(tracker.Update(settings, 0.10, otherAxesActive: false));
        Assert.True(tracker.Update(settings, 0.11, otherAxesActive: false));
        Assert.True(tracker.IsIntentional);
    }

    [Fact]
    public void StrongDeflection_IsImmediateIntent()
    {
        var tracker = new AxisIntentTracker();
        var settings = new AxisIntentSettings
        {
            Enabled = true,
            DeflectionThreshold = 0.07,
            StrongDeflectionThreshold = 0.20,
            ConfirmSamples = 3
        };

        Assert.True(tracker.Update(settings, 0.35, otherAxesActive: false));
        Assert.True(tracker.IsIntentional);
    }
}
