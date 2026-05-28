using JoystickInputTuner.Core.Models;

namespace JoystickInputTuner.Core.Tests;

public class FilterSettingsSnapshotTests
{
    [Fact]
    public void Clone_PreservesUltraSpikeAndShieldFields()
    {
        var source = new FilterSettings
        {
            SpikeGate = new SpikeGateSettings
            {
                UltraSpikeGuardEnabled = true,
                UltraSpikeDeltaThreshold = 0.012,
                SwingBypassEnabled = true,
                SwingBypassDeltaThreshold = 0.52
            },
            CrossAxisShield = new CrossAxisShieldSettings
            {
                MinOtherAxisActiveSamples = 1,
                BlockEngageRatio = 0.65,
                LockLeakMultiplier = 0.0,
                RequireOtherAxisDominance = false
            }
        };

        var clone = FilterSettingsSnapshot.Clone(source);

        Assert.Equal(0.012, clone.SpikeGate.UltraSpikeDeltaThreshold, 4);
        Assert.Equal(0.52, clone.SpikeGate.SwingBypassDeltaThreshold, 3);
        Assert.Equal(1, clone.CrossAxisShield.MinOtherAxisActiveSamples);
        Assert.Equal(0.65, clone.CrossAxisShield.BlockEngageRatio, 3);
        Assert.False(clone.CrossAxisShield.RequireOtherAxisDominance);
    }
}
