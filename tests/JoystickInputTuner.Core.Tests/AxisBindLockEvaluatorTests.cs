using JoystickInputTuner.Core.Filters;
using JoystickInputTuner.Core.Models;

namespace JoystickInputTuner.Core.Tests;

public sealed class AxisBindLockEvaluatorTests
{
    [Fact]
    public void ToggleMode_FlipsOnRisingEdge()
    {
        var settings = new AxisBindLockSettings
        {
            Enabled = true,
            BindDeviceKind = "Joystick",
            BindDeviceId = "device-a",
            ButtonIndex = 0,
        };
        var latched = false;
        var previous = false;

        Assert.True(AxisBindLockEvaluator.UpdateLockActive(settings, true, ref latched, ref previous));
        Assert.True(latched);

        Assert.True(AxisBindLockEvaluator.UpdateLockActive(settings, false, ref latched, ref previous));

        Assert.False(AxisBindLockEvaluator.UpdateLockActive(settings, true, ref latched, ref previous));
    }

    [Fact]
    public void NotBound_WhenDeviceMissing()
    {
        var settings = new AxisBindLockSettings { Enabled = true, ButtonIndex = 2, BindDeviceId = "" };
        var latched = true;
        var previous = true;

        Assert.False(AxisBindLockEvaluator.UpdateLockActive(settings, true, ref latched, ref previous));
    }

    [Fact]
    public void KeyboardBound_WhenKeyCodeSet()
    {
        var settings = new AxisBindLockSettings
        {
            Enabled = true,
            BindDeviceKind = "Keyboard",
            KeyCode = 44,
        };
        Assert.True(AxisBindLockEvaluator.IsBound(settings));

        var latched = false;
        var previous = false;
        Assert.True(AxisBindLockEvaluator.UpdateLockActive(settings, true, ref latched, ref previous));
    }

    [Fact]
    public void HasBindAssignment_WithoutEnabled()
    {
        var settings = new AxisBindLockSettings
        {
            Enabled = false,
            BindDeviceKind = "Joystick",
            BindDeviceId = "device-a",
            ButtonIndex = 1,
        };
        Assert.True(AxisBindLockEvaluator.HasBindAssignment(settings));
        Assert.False(AxisBindLockEvaluator.IsBound(settings));
    }
}
