using JoystickInputTuner.Core.Filters;
using JoystickInputTuner.Core.Models;

namespace JoystickInputTuner.Core.Tests;

public class CrossAxisShieldActivityTests
{
    [Fact]
    public void Activates_AfterSustainedSamples()
    {
        var shield = CreateShield();
        var latched = false;
        var sustained = 0;
        var current = new Dictionary<string, double> { ["X"] = 0.20, ["Y"] = 0.0, ["RZ"] = 0.0 };
        var previous = new Dictionary<string, double> { ["X"] = 0.19, ["Y"] = 0.0, ["RZ"] = 0.0 };

        Assert.False(CrossAxisShieldActivity.Evaluate(
            shield, "RZ", "RZ", current, previous, 0.005, ref latched, ref sustained));

        for (var i = 0; i < 3; i++)
            CrossAxisShieldActivity.Evaluate(shield, "RZ", "RZ", current, previous, 0.005, ref latched, ref sustained);

        var active = CrossAxisShieldActivity.Evaluate(
            shield, "RZ", "RZ", current, previous, 0.005, ref latched, ref sustained);

        Assert.True(active);
        Assert.True(latched);
    }

    [Fact]
    public void Ignores_SingleSampleBlip()
    {
        var shield = CreateShield();
        var latched = false;
        var sustained = 0;
        var blip = new Dictionary<string, double> { ["X"] = 0.20, ["Y"] = 0.0, ["RZ"] = 0.0 };
        var idle = new Dictionary<string, double> { ["X"] = 0.0, ["Y"] = 0.0, ["RZ"] = 0.0 };

        CrossAxisShieldActivity.Evaluate(shield, "RZ", "RZ", blip, idle, 0.005, ref latched, ref sustained);
        var after = CrossAxisShieldActivity.Evaluate(shield, "RZ", "RZ", idle, blip, 0.005, ref latched, ref sustained);

        Assert.False(after);
    }

    private static CrossAxisShieldSettings CreateShield() => new()
    {
        Enabled = true,
        TargetAxisId = "RZ",
        WatchedAxes = ["X", "Y"],
        OtherAxisDeflectionThreshold = 0.10,
        MinOtherAxisActiveSamples = 4,
        MinOtherAxisVelocityPerSecond = 0.25
    };
}
