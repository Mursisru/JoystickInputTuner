using JoystickInputTuner.Core.Models;

namespace JoystickInputTuner.Core.Tests;

public sealed class FilterSettingsNormalizerTests
{
    [Fact]
    public void WatchedAxes_PreservesNonLinearAxes()
    {
        var settings = FilterSettingsNormalizer.Ensure(new FilterSettings
        {
            CrossAxisShield = new CrossAxisShieldSettings
            {
                WatchedAxes = ["X", "Z", "RZ"],
            },
            AxisBindLock = new AxisBindLockSettings
            {
                Enabled = true,
                BindDeviceKind = "Keyboard",
                KeyCode = 32,
            },
        });

        Assert.Equal(["X", "Z", "RZ"], settings.CrossAxisShield.WatchedAxes);
        Assert.Equal("Keyboard", settings.AxisBindLock.BindDeviceKind);
        Assert.Equal(32, settings.AxisBindLock.KeyCode);
    }
}
