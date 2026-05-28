using JoystickInputTuner.Core.Models;

namespace JoystickInputTuner.Core.Tests;

public class FilterSettingsNormalizerTests
{
    [Fact]
    public void Ensure_FillsMissingAxisIntent()
    {
        var settings = new FilterSettings { AxisIntent = null! };
        var normalized = FilterSettingsNormalizer.Ensure(settings);
        Assert.NotNull(normalized.AxisIntent);
        Assert.True(normalized.AxisIntent.Enabled);
    }
}
