using JoystickInputTuner.Core.Filters;
using JoystickInputTuner.Core.Models;

namespace JoystickInputTuner.Core.Tests;

public class CrossAxisShieldPolicyTests
{
    [Fact]
    public void BlockStrength_RampsGradually()
    {
        var shield = CreateShield();
        shield.HardLockWhenActive = false;
        var low = CrossAxisShieldPolicy.ComputeBlockStrength(shield, true, false, 0.08);
        var mid = CrossAxisShieldPolicy.ComputeBlockStrength(shield, true, false, 0.16);
        var full = CrossAxisShieldPolicy.ComputeBlockStrength(shield, true, false, 0.30);

        Assert.Equal(0.0, low, 3);
        Assert.InRange(mid, 0.2, 0.9);
        Assert.Equal(1.0, full, 3);
    }

    [Fact]
    public void BlockStrength_Zero_WhenIntentWhileOthersActive()
    {
        var shield = CreateShield();
        var strength = CrossAxisShieldPolicy.ComputeBlockStrength(shield, true, true, 0.30);
        Assert.Equal(0.0, strength, 3);
    }

    [Fact]
    public void BlockStrength_PreLatchBeforeFullLatch()
    {
        var shield = CreateShield();
        shield.HardLockWhenActive = false;
        var peak = 0.16;
        var none = CrossAxisShieldPolicy.ComputeBlockStrength(shield, false, false, peak, sustainedSampleCount: 0);
        var partial = CrossAxisShieldPolicy.ComputeBlockStrength(shield, false, false, peak, sustainedSampleCount: 2);
        var fullLatch = CrossAxisShieldPolicy.ComputeBlockStrength(shield, true, false, peak);

        Assert.Equal(0.0, none, 3);
        Assert.InRange(partial, 0.05, fullLatch);
        Assert.True(partial < fullLatch);
    }

    [Fact]
    public void BlockStrength_HardLock_FullBlockOnFirstSampleAboveThreshold()
    {
        var shield = CreateShield();
        var strength = CrossAxisShieldPolicy.ComputeBlockStrength(shield, false, false, 0.12, sustainedSampleCount: 1);
        Assert.Equal(1.0, strength, 3);
    }

    [Fact]
    public void BlockStrength_Dominance_AllowsIntentionalYawAlone()
    {
        var shield = CreateShield();
        shield.RequireOtherAxisDominance = true;
        shield.OtherAxisDominanceRatio = 1.15;
        var axes = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["X"] = 0.02,
            ["Y"] = 0.01,
            ["RZ"] = 0.45
        };

        var strength = CrossAxisShieldPolicy.ComputeBlockStrength(
            shield,
            otherAxesActive: false,
            targetIntentActive: false,
            otherAxesPeakDeflection: 0.02,
            sustainedSampleCount: 1,
            targetAxisDeflection: 0.45,
            axes,
            "RZ");

        Assert.Equal(0.0, strength, 3);
    }

    [Fact]
    public void BlockStrength_Dominance_BlocksCorrelatedCrosstalkSpike()
    {
        var shield = CreateShield();
        shield.RequireOtherAxisDominance = true;
        shield.OtherAxisDominanceRatio = 1.15;
        var axes = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["X"] = 0.3214,
            ["Y"] = -0.0949,
            ["RZ"] = -0.3203
        };

        var strength = CrossAxisShieldPolicy.ComputeBlockStrength(
            shield,
            otherAxesActive: true,
            targetIntentActive: false,
            otherAxesPeakDeflection: 0.3214,
            sustainedSampleCount: 4,
            targetAxisDeflection: -0.3203,
            axes,
            "RZ");

        Assert.Equal(1.0, strength, 3);
    }

    [Fact]
    public void ApplyHardLockOutput_SnapsToAnchorWhenEngaged()
    {
        var shield = CreateShield();
        var locked = CrossAxisShieldPolicy.ApplyHardLockOutput(shield, 0.5, 0.18, 0.0);
        Assert.Equal(0.0, locked, 4);
    }

    [Fact]
    public void BlockStrength_FullAtPeakWithoutLatch()
    {
        var shield = CreateShield();
        var strength = CrossAxisShieldPolicy.ComputeBlockStrength(shield, false, false, 0.30, sustainedSampleCount: 1);
        Assert.Equal(1.0, strength, 3);
    }

    [Fact]
    public void PeakDeflection_IgnoresSaturatedWatchedAxes()
    {
        var shield = CreateShield();
        shield.WatchedAxes = ["X", "Y", "RX"];
        var axes = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["X"] = 0.01,
            ["Y"] = -0.012,
            ["RX"] = 1.0,
            ["RZ"] = 0.0
        };

        var peak = OtherAxisActivityTracker.GetPeakDeflection(shield, "RZ", axes);
        Assert.InRange(peak, 0.01, 0.02);
    }

    [Fact]
    public void LockSmoother_SnapsInstantlyAtFullBlockWithZeroLeak()
    {
        var smoother = new CrossAxisLockSmoother();
        var shield = CreateShield();
        smoother.Reset(0.42);

        var locked = smoother.Process(shield, 1.0, 0.42, 0.0, 0.005);
        Assert.Equal(0.0, locked, 4);
    }
    [Fact]
    public void LockSmoother_SoftensPartialEngage()
    {
        var smoother = new CrossAxisLockSmoother();
        var shield = CreateShield();
        shield.LockLeakMultiplier = 0.20;
        smoother.Reset(0.0);

        var s1 = smoother.Process(shield, 0.5, 0.5, 0.0, 0.005);
        var s2 = smoother.Process(shield, 0.5, 0.5, 0.0, 0.005);

        Assert.True(Math.Abs(s1) < 0.5);
        Assert.True(Math.Abs(s2) < Math.Abs(s1) + 0.001 || Math.Abs(s2) < 0.25);
    }

    [Fact]
    public void Intent_NotInstant_WhenOthersActiveAndStrongSpike()
    {
        var tracker = new AxisIntentTracker();
        var settings = new AxisIntentSettings
        {
            Enabled = true,
            StrongDeflectionThreshold = 0.35,
            DisableInstantIntentWhileOthersActive = true,
            ConfirmSamplesWhileOthersActive = 4
        };

        Assert.False(tracker.Update(settings, 0.9, otherAxesActive: true));
        Assert.False(tracker.Update(settings, 0.9, otherAxesActive: true));
        Assert.False(tracker.Update(settings, 0.9, otherAxesActive: true));
    }

    private static CrossAxisShieldSettings CreateShield() => new()
    {
        Enabled = true,
        OtherAxisDeflectionThreshold = 0.10,
        BlockEngageRatio = 0.85,
        FullBlockDeflectionMultiplier = 2.2,
        HardLockWhenActive = true,
        HardLockForceCenter = true,
        LockLeakMultiplier = 0.0,
        ParasiticClampLeakMultiplier = 0.0,
        RespectTargetIntent = true,
        LockSmoothingSeconds = 0.055
    };
}
