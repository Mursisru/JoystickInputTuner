using JoystickInputTuner.Core.Filters;
using JoystickInputTuner.Core.Models;
using JoystickInputTuner.Core.Services;

namespace JoystickInputTuner.Core.Tests;

public class FilterPipelineTests
{
    [Fact]
    public void SpikeGate_SuppressesSingleLargeJump()
    {
        var pipeline = new FilterPipeline();
        pipeline.Settings.Deadzone.Enabled = false;
        pipeline.Settings.Median.Enabled = false;
        pipeline.Settings.Hampel.Enabled = false;
        pipeline.Settings.RateLimiter.Enabled = false;
        pipeline.Settings.Ema.Enabled = false;
        pipeline.Settings.SpikeGate.Enabled = true;
        pipeline.Settings.SpikeGate.RadialZonesEnabled = false;
        pipeline.Settings.SpikeGate.DeltaThreshold = 0.2;
        pipeline.Settings.SpikeGate.VelocityThresholdPerSecond = 50;

        pipeline.Reset(0);
        _ = pipeline.Process(0.0);
        var sample = pipeline.Process(0.9);

        Assert.True(sample.SpikeSuppressed);
        Assert.Equal(0.0, sample.FilteredValue, 3);
        Assert.Equal(1, pipeline.Statistics.SpikeSuppressedCount);
    }

    [Fact]
    public void SpikeGate_DoesNotLatch_WhenInputStepPersists()
    {
        var pipeline = new FilterPipeline();
        pipeline.Settings.Deadzone.Enabled = false;
        pipeline.Settings.Median.Enabled = false;
        pipeline.Settings.Hampel.Enabled = false;
        pipeline.Settings.RateLimiter.Enabled = false;
        pipeline.Settings.Ema.Enabled = false;
        pipeline.Settings.SpikeGate.Enabled = true;
        pipeline.Settings.SpikeGate.RadialZonesEnabled = false;
        pipeline.Settings.SpikeGate.DeltaThreshold = 0.2;
        pipeline.Settings.SpikeGate.VelocityThresholdPerSecond = 40;
        pipeline.Settings.SpikeGate.MaxConsecutiveSuppressions = 1;

        pipeline.Reset(0);
        _ = pipeline.Process(0.0);
        var suppressed = pipeline.Process(0.9);
        var accepted = pipeline.Process(0.9);

        Assert.True(suppressed.SpikeSuppressed);
        Assert.Equal(0.0, suppressed.FilteredValue, 3);
        Assert.False(accepted.SpikeSuppressed);
        Assert.Equal(0.9, accepted.FilteredValue, 3);
    }

    [Fact]
    public void SpikeGate_DoesNotSuppress_VelocityOnlyChange()
    {
        var pipeline = new FilterPipeline();
        pipeline.Settings.Deadzone.Enabled = false;
        pipeline.Settings.Median.Enabled = false;
        pipeline.Settings.Hampel.Enabled = false;
        pipeline.Settings.RateLimiter.Enabled = false;
        pipeline.Settings.Ema.Enabled = false;
        pipeline.Settings.ZImpulseGuard.Enabled = false;
        pipeline.Settings.SpikeGate.Enabled = true;
        pipeline.Settings.SpikeGate.RadialZonesEnabled = false;
        pipeline.Settings.SpikeGate.DeltaThreshold = 0.2;
        pipeline.Settings.SpikeGate.VelocityThresholdPerSecond = 6;

        pipeline.Reset(0);
        _ = pipeline.Process(0.0);
        var sample = pipeline.Process(0.12);

        Assert.False(sample.SpikeSuppressed);
        Assert.Equal(0.12, sample.FilteredValue, 3);
    }

    [Fact]
    public void Ema_AdaptiveAlpha_CatchesLargeStepQuickly()
    {
        var pipeline = new FilterPipeline();
        pipeline.Settings.Deadzone.Enabled = false;
        pipeline.Settings.Median.Enabled = false;
        pipeline.Settings.Hampel.Enabled = false;
        pipeline.Settings.SpikeGate.Enabled = false;
        pipeline.Settings.RateLimiter.Enabled = false;
        pipeline.Settings.Ema.Enabled = true;
        pipeline.Settings.Ema.Alpha = 0.2;

        pipeline.Reset(0);
        _ = pipeline.Process(0.0);
        var first = pipeline.Process(1.0);

        Assert.Equal(1.0, first.FilteredValue, 3);
    }

    [Fact]
    public void Ema_SmoothsSmallChanges()
    {
        var pipeline = new FilterPipeline();
        pipeline.Settings.Deadzone.Enabled = false;
        pipeline.Settings.Median.Enabled = false;
        pipeline.Settings.Hampel.Enabled = false;
        pipeline.Settings.SpikeGate.Enabled = false;
        pipeline.Settings.RateLimiter.Enabled = false;
        pipeline.Settings.Ema.Enabled = true;
        pipeline.Settings.Ema.Alpha = 0.2;

        pipeline.Reset(0.5);
        _ = pipeline.Process(0.5);
        var sample = pipeline.Process(0.52);

        Assert.InRange(sample.FilteredValue, 0.503, 0.507);
    }

    [Fact]
    public void Hampel_BypassesDuringRapidRamp_DoesNotHoldHighMedian()
    {
        var pipeline = new FilterPipeline();
        pipeline.Settings.Deadzone.Enabled = false;
        pipeline.Settings.Median.Enabled = false;
        pipeline.Settings.SpikeGate.Enabled = false;
        pipeline.Settings.RateLimiter.Enabled = false;
        pipeline.Settings.Ema.Enabled = false;
        pipeline.Settings.Hampel.Enabled = true;
        pipeline.Settings.Hampel.WindowSize = 9;
        pipeline.Settings.Hampel.SigmaMultiplier = 3.0;

        pipeline.Reset(1.0);
        for (var v = 1.0; v >= 0.0; v -= 0.15)
            _ = pipeline.Process(v);

        var sample = pipeline.Process(0.0);
        Assert.InRange(sample.FilteredValue, -0.15, 0.15);
        Assert.True(pipeline.Statistics.HampelOutlierCount < 5);
    }

    [Fact]
    public void FullPipeline_ReturnsToCenterFaster_WhenRawReleased()
    {
        var pipeline = new FilterPipeline();
        pipeline.LoadSettings(new FilterSettings
        {
            Deadzone = new() { Enabled = true, Radius = 0.03 },
            Median = new() { Enabled = true, WindowSize = 5 },
            Hampel = new() { Enabled = true, WindowSize = 9 },
            SpikeGate = new()
            {
                Enabled = true,
                RadialZonesEnabled = false,
                DeltaThreshold = 0.35,
                VelocityThresholdPerSecond = 6.0,
                MaxConsecutiveSuppressions = 1
            },
            RateLimiter = new() { Enabled = true, MaxDeltaPerSecond = 8.0 },
            Ema = new() { Enabled = true, Alpha = 0.40 }
        });

        pipeline.Reset(0);
        for (var i = 0; i < 30; i++)
            _ = pipeline.Process(0.95);

        for (var i = 0; i < 40; i++)
            _ = pipeline.Process(0.0);

        var sample = pipeline.Process(0.0);
        Assert.InRange(Math.Abs(sample.FilteredValue), 0.0, 0.12);
    }

    [Fact]
    public void FullPipeline_StaysStable_WhenRawIsFlatNearCenter()
    {
        var pipeline = new FilterPipeline();
        pipeline.LoadSettings(new FilterSettings
        {
            Deadzone = new() { Enabled = true, Radius = 0.03 },
            Median = new() { Enabled = true, WindowSize = 5 },
            Hampel = new() { Enabled = true, WindowSize = 9 },
            SpikeGate = new()
            {
                Enabled = true,
                RadialZonesEnabled = false,
                DeltaThreshold = 0.35,
                VelocityThresholdPerSecond = 6.0,
                MaxConsecutiveSuppressions = 1
            },
            RateLimiter = new() { Enabled = true, MaxDeltaPerSecond = 8.0 },
            Ema = new() { Enabled = true, Alpha = 0.40 }
        });

        pipeline.Reset(0);
        for (var i = 0; i < 120; i++)
            _ = pipeline.Process(0.0);

        var max = 0.0;
        for (var i = 0; i < 200; i++)
        {
            var sample = pipeline.Process(0.0);
            max = Math.Max(max, Math.Abs(sample.FilteredValue));
        }

        Assert.True(max < 0.06, $"Filtered output oscillated to {max:0.000}");
    }

    [Fact]
    public void SpikeGate_CenterSmoothingMultiplier_LowersEffectiveThresholds()
    {
        var mild = new SpikeGateSettings
        {
            CenterDeltaThreshold = 0.10,
            CenterVelocityThresholdPerSecond = 3.6,
            CenterMaxConsecutiveSuppressions = 4,
            CenterSmoothingMultiplier = 1.0
        };
        var strong = new SpikeGateSettings
        {
            CenterDeltaThreshold = 0.10,
            CenterVelocityThresholdPerSecond = 3.6,
            CenterMaxConsecutiveSuppressions = 4,
            CenterSmoothingMultiplier = 2.0
        };

        var mildEffective = SpikeGateFilter.GetEffectiveCenterThresholds(mild);
        var strongEffective = SpikeGateFilter.GetEffectiveCenterThresholds(strong);

        Assert.True(strongEffective.DeltaThreshold < mildEffective.DeltaThreshold);
        Assert.True(strongEffective.VelocityThreshold < mildEffective.VelocityThreshold);
        Assert.True(strongEffective.MaxConsecutive > mildEffective.MaxConsecutive);
    }

    [Fact]
    public void SpikeGate_Radial_Returning_KeepsOuterRulesWhileOutputLags()
    {
        var settings = new SpikeGateSettings
        {
            RadialZonesEnabled = true,
            CenterZoneEnd = 0.28,
            CenterDeltaThreshold = 0.05,
            CenterVelocityThresholdPerSecond = 1.8,
            CenterMaxConsecutiveSuppressions = 5,
            OuterDeltaThreshold = 0.38,
            OuterVelocityThresholdPerSecond = 11.0,
            OuterMaxConsecutiveSuppressions = 1,
            ZoneBlendWidth = 0.15
        };

        var (hitAtCenter, _) = SpikeGateFilter.EvaluateSpike(0.12, 4.0, 0.06, settings);
        var (hitWhileReturning, _) = SpikeGateFilter.EvaluateSpike(0.12, 4.0, 0.45, settings);

        Assert.True(hitAtCenter);
        Assert.False(hitWhileReturning);
    }

    [Fact]
    public void SpikeGate_Radial_SuppressesSharpMicroMoveNearCenter()
    {
        var pipeline = new FilterPipeline();
        pipeline.Settings.Deadzone.Enabled = false;
        pipeline.Settings.Median.Enabled = false;
        pipeline.Settings.Hampel.Enabled = false;
        pipeline.Settings.RateLimiter.Enabled = false;
        pipeline.Settings.Ema.Enabled = false;
        pipeline.Settings.SpikeGate.Enabled = true;
        pipeline.Settings.SpikeGate.RadialZonesEnabled = true;
        pipeline.Settings.SpikeGate.CenterDeltaThreshold = 0.05;
        pipeline.Settings.SpikeGate.CenterVelocityThresholdPerSecond = 1.8;
        pipeline.Settings.SpikeGate.CenterMaxConsecutiveSuppressions = 5;

        pipeline.Reset(0.02);
        for (var i = 0; i < 5; i++)
            _ = pipeline.Process(0.02);

        var sample = pipeline.Process(0.16);
        Assert.True(sample.SpikeSuppressed);
        Assert.True(Math.Abs(sample.FilteredValue) < 0.10);
    }

    [Fact]
    public void SpikeGate_UltraSpike_SuppressesFastMicroImpulseNearCenter()
    {
        var settings = new SpikeGateSettings
        {
            Enabled = true,
            RadialZonesEnabled = true,
            UltraSpikeGuardEnabled = true,
            UltraSpikeCenterMaxAbs = 0.22,
            UltraSpikeDeltaThreshold = 0.016,
            UltraSpikeVelocityThresholdPerSecond = 2.6,
            UltraSpikeMaxConsecutiveSuppressions = 12,
            CenterDeltaThreshold = 0.05,
            CenterVelocityThresholdPerSecond = 4.0,
            CenterMaxConsecutiveSuppressions = 1
        };

        var (hit, maxSuppress) = SpikeGateFilter.EvaluateSpike(0.022, 3.2, 0.04, settings);
        Assert.True(hit);
        Assert.Equal(12, maxSuppress);
    }

    [Fact]
    public void OutputSettle_SnapsCenterRippleToZero()
    {
        var filter = new OutputSettleFilter();
        var context = new FilterContext
        {
            Settings = new FilterSettings(),
            Statistics = new FilterStatistics(),
            DeltaTime = TimeSpan.FromMilliseconds(6),
            RawInput = 0.0
        };

        filter.Reset(0);
        _ = filter.Process(0.028, context);
        var settled = filter.Process(0.031, context);
        Assert.Equal(0.0, settled, 3);
    }

    [Fact]
    public void RateLimiter_SnapsToZeroWhenRawAndOutputNearCenter()
    {
        var filter = new RateLimiterFilter();
        var context = new FilterContext
        {
            Settings = new FilterSettings { RateLimiter = new RateLimiterSettings { Enabled = true, MaxDeltaPerSecond = 10 } },
            Statistics = new FilterStatistics(),
            DeltaTime = TimeSpan.FromMilliseconds(6),
            RawInput = 0.0
        };

        filter.Reset(0.04);
        var value = filter.Process(0.0, context);
        Assert.Equal(0.0, value, 3);
    }

    [Fact]
    public void SpikeGate_RailHold_SuppressesFullScaleAlternation()
    {
        var settings = new SpikeGateSettings
        {
            Enabled = true,
            RadialZonesEnabled = true,
            RailHoldEnabled = true,
            RailThreshold = 0.90,
            RailFlipMinDelta = 0.85
        };
        var filter = new SpikeGateFilter();
        var context = new FilterContext
        {
            Settings = new FilterSettings { SpikeGate = settings },
            Statistics = new FilterStatistics(),
            DeltaTime = TimeSpan.FromMilliseconds(6),
            RawInput = 0.98
        };

        filter.Reset(0.0);
        _ = filter.Process(0.98, context);
        var flipContext = new FilterContext
        {
            Settings = context.Settings,
            Statistics = context.Statistics,
            DeltaTime = context.DeltaTime,
            RawInput = -0.98
        };
        var held = filter.Process(-0.98, flipContext);
        Assert.True(context.Statistics.SpikeSuppressedCount > 0);
        Assert.InRange(held, -0.05, 0.05);
    }

    [Fact]
    public void SpikeGate_SwingBypass_AllowsWideFastMove()
    {
        var settings = new SpikeGateSettings
        {
            RadialZonesEnabled = true,
            SwingBypassEnabled = true,
            SwingBypassMinAbs = 0.32,
            SwingBypassDeltaThreshold = 0.52,
            SwingBypassVelocityThresholdPerSecond = 18.0,
            UltraSpikeGuardEnabled = true,
            UltraSpikeCenterMaxAbs = 0.20,
            UltraSpikeDeltaThreshold = 0.012,
            UltraSpikeVelocityThresholdPerSecond = 2.2
        };

        var (hit, _) = SpikeGateFilter.EvaluateSpike(0.18, 9.0, 0.45, settings);
        Assert.False(hit);
    }

    [Fact]
    public void SpikeGate_SwingBypass_StillBlocksExtremeSpike()
    {
        var settings = new SpikeGateSettings
        {
            RadialZonesEnabled = true,
            SwingBypassEnabled = true,
            SwingBypassMinAbs = 0.32,
            SwingBypassDeltaThreshold = 0.52,
            SwingBypassVelocityThresholdPerSecond = 18.0
        };

        var (hit, _) = SpikeGateFilter.EvaluateSpike(0.60, 25.0, 0.50, settings);
        Assert.True(hit);
    }

    [Fact]
    public void SpikeGate_Radial_AllowsGradualMoveAtOuterZone()
    {
        var pipeline = new FilterPipeline();
        pipeline.Settings.Deadzone.Enabled = false;
        pipeline.Settings.Median.Enabled = false;
        pipeline.Settings.Hampel.Enabled = false;
        pipeline.Settings.RateLimiter.Enabled = false;
        pipeline.Settings.Ema.Enabled = false;
        pipeline.Settings.SpikeGate.Enabled = true;
        pipeline.Settings.SpikeGate.RadialZonesEnabled = true;
        pipeline.Settings.SpikeGate.OuterDeltaThreshold = 0.38;
        pipeline.Settings.SpikeGate.OuterVelocityThresholdPerSecond = 11.0;

        pipeline.Reset(0);
        for (var v = 0.0; v <= 0.70; v += 0.07)
            _ = pipeline.Process(v);

        var sample = pipeline.Process(0.77);
        Assert.False(sample.SpikeSuppressed);
        Assert.InRange(sample.FilteredValue, 0.70, 0.85);
    }

    [Fact]
    public void RateLimiter_DisablesCatchUp_WhenCrossAxisBlockActive()
    {
        var pipeline = new FilterPipeline();
        pipeline.Settings.Deadzone.Enabled = false;
        pipeline.Settings.Median.Enabled = false;
        pipeline.Settings.Hampel.Enabled = false;
        pipeline.Settings.SpikeGate.Enabled = false;
        pipeline.Settings.ZImpulseGuard.Enabled = false;
        pipeline.Settings.Ema.Enabled = false;
        pipeline.Settings.RateLimiter.Enabled = true;
        pipeline.Settings.RateLimiter.MaxDeltaPerSecond = 4.0;
        pipeline.CrossAxisBlockStrength = 0.8;

        pipeline.Reset(0.0);
        _ = pipeline.Process(0.0);
        var sample = pipeline.Process(0.25);
        Assert.InRange(sample.FilteredValue, 0.0, 0.03);
    }

    [Fact]
    public void ZImpulseGuard_BlocksSingleNearCenterImpulse()
    {
        var pipeline = new FilterPipeline();
        pipeline.Settings.Deadzone.Enabled = false;
        pipeline.Settings.Median.Enabled = false;
        pipeline.Settings.Hampel.Enabled = false;
        pipeline.Settings.SpikeGate.Enabled = false;
        pipeline.Settings.RateLimiter.Enabled = false;
        pipeline.Settings.Ema.Enabled = false;
        pipeline.Settings.ZImpulseGuard.Enabled = true;
        pipeline.Settings.ZImpulseGuard.CenterRadius = 0.35;
        pipeline.Settings.ZImpulseGuard.IntentThreshold = 0.06;
        pipeline.Settings.ZImpulseGuard.ConfirmSamples = 2;

        pipeline.Reset(0.0);
        _ = pipeline.Process(0.0);
        var impulse = pipeline.Process(-0.20);

        Assert.Equal(0.0, impulse.FilteredValue, 3);
    }

    [Fact]
    public void ZImpulseGuard_AllowsSustainedNearCenterMoveAfterConfirmation()
    {
        var pipeline = new FilterPipeline();
        pipeline.Settings.Deadzone.Enabled = false;
        pipeline.Settings.Median.Enabled = false;
        pipeline.Settings.Hampel.Enabled = false;
        pipeline.Settings.SpikeGate.Enabled = false;
        pipeline.Settings.RateLimiter.Enabled = false;
        pipeline.Settings.Ema.Enabled = false;
        pipeline.Settings.ZImpulseGuard.Enabled = true;
        pipeline.Settings.ZImpulseGuard.CenterRadius = 0.35;
        pipeline.Settings.ZImpulseGuard.IntentThreshold = 0.06;
        pipeline.Settings.ZImpulseGuard.ConfirmSamples = 2;

        pipeline.Reset(0.0);
        _ = pipeline.Process(0.0);
        _ = pipeline.Process(-0.20);
        var accepted = pipeline.Process(-0.20);

        Assert.Equal(-0.20, accepted.FilteredValue, 3);
    }

    [Fact]
    public void Deadzone_ClampsNearCenterNoiseToZero()
    {
        var pipeline = new FilterPipeline();
        pipeline.Settings.Deadzone.Enabled = true;
        pipeline.Settings.Deadzone.Dynamic = false;
        pipeline.Settings.Deadzone.Radius = 0.05;
        pipeline.Settings.Median.Enabled = false;
        pipeline.Settings.Hampel.Enabled = false;
        pipeline.Settings.SpikeGate.Enabled = false;
        pipeline.Settings.RateLimiter.Enabled = false;
        pipeline.Settings.Ema.Enabled = false;

        pipeline.Reset(0);
        var sample = pipeline.Process(0.03);

        Assert.Equal(0.0, sample.FilteredValue, 6);
    }

}
