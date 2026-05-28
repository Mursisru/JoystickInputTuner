using JoystickInputTuner.Core.Models;

namespace JoystickInputTuner.Core.Filters;

public sealed class SpikeGateFilter : IInputFilter
{
    private double _lastValue;
    private double _lastRawInput;
    private int _suppressedInRow;
    private int _lastSuppressedDirection;

    public double Process(double value, FilterContext context)
    {
        var settings = context.Settings.SpikeGate;
        if (!settings.Enabled)
        {
            _lastValue = value;
            _lastRawInput = context.RawInput;
            return value;
        }

        if (TryHoldRailAlternation(context.RawInput, settings, context))
            return _lastValue;

        var delta = value - _lastValue;
        var dt = Math.Max(1e-6, context.DeltaTime.TotalSeconds);
        var velocity = Math.Abs(delta) / dt;
        var signalDistance = FilterRadial.SignalDistance(value, _lastValue);
        var zoneDistance = FilterRadial.ResolveZoneDistance(context.RawInput, signalDistance);

        var (shouldSuppress, maxSuppressions) = EvaluateSpike(delta, velocity, zoneDistance, settings);

        if (shouldSuppress)
        {
            var direction = Math.Sign(delta);
            if (direction != 0 && direction == _lastSuppressedDirection)
                _suppressedInRow++;
            else
                _suppressedInRow = 1;

            _lastSuppressedDirection = direction;
            maxSuppressions = Math.Clamp(maxSuppressions, 1, 8);
            if (_suppressedInRow > maxSuppressions)
            {
                _suppressedInRow = 0;
                _lastSuppressedDirection = 0;
                _lastValue = value;
                return value;
            }

            _lastRawInput = context.RawInput;
            context.Statistics.RegisterSpikeSuppressed();
            return _lastValue;
        }

        _suppressedInRow = 0;
        _lastSuppressedDirection = 0;
        _lastValue = value;
        _lastRawInput = context.RawInput;
        return value;
    }

    internal static bool IsRailAlternation(double raw, double previousRaw, SpikeGateSettings settings)
    {
        if (!settings.RailHoldEnabled)
            return false;

        var threshold = Math.Clamp(settings.RailThreshold, 0.75, 0.999);
        var minFlipDelta = Math.Clamp(settings.RailFlipMinDelta, 0.5, 2.0);
        if (Math.Abs(raw) < threshold || Math.Abs(previousRaw) < threshold)
            return false;

        if (Math.Sign(raw) == Math.Sign(previousRaw))
            return false;

        return Math.Abs(raw - previousRaw) >= minFlipDelta;
    }

    private bool TryHoldRailAlternation(double raw, SpikeGateSettings settings, FilterContext context)
    {
        if (!IsRailAlternation(raw, _lastRawInput, settings))
            return false;

        _lastRawInput = raw;
        _suppressedInRow = 0;
        _lastSuppressedDirection = 0;
        context.Statistics.RegisterSpikeSuppressed();
        return true;
    }

    internal static (bool ShouldSuppress, int MaxConsecutive) EvaluateSpike(
        double delta,
        double velocity,
        double zoneDistance,
        SpikeGateSettings settings)
    {
        if (settings.RadialZonesEnabled &&
            settings.UltraSpikeGuardEnabled &&
            zoneDistance <= Math.Clamp(settings.UltraSpikeCenterMaxAbs, 0.05, 0.5))
        {
            var ultraDelta = Math.Clamp(settings.UltraSpikeDeltaThreshold, 0.004, 0.2);
            var ultraVelocity = Math.Clamp(settings.UltraSpikeVelocityThresholdPerSecond, 0.5, 80.0);
            var ultraMax = Math.Clamp(settings.UltraSpikeMaxConsecutiveSuppressions, 1, 16);
            var absDelta = Math.Abs(delta);
            if (absDelta >= ultraDelta && velocity >= ultraVelocity)
                return (true, ultraMax);

            if (velocity >= ultraVelocity * 1.25 && absDelta >= ultraDelta * 0.40)
                return (true, ultraMax);

            if (absDelta >= ultraDelta * 0.75 && velocity >= ultraVelocity * 0.85)
                return (true, ultraMax);
        }

        if (settings.RadialZonesEnabled &&
            settings.SwingBypassEnabled &&
            zoneDistance >= Math.Clamp(settings.SwingBypassMinAbs, 0.15, 0.95))
        {
            var swingDelta = Math.Clamp(settings.SwingBypassDeltaThreshold, 0.2, 1.0);
            var swingVelocity = Math.Clamp(settings.SwingBypassVelocityThresholdPerSecond, 5.0, 120.0);
            var swingMax = Math.Clamp(settings.SwingBypassMaxConsecutiveSuppressions, 1, 3);
            var swingHit = Math.Abs(delta) >= swingDelta && velocity >= swingVelocity;
            return (swingHit, swingMax);
        }

        if (!settings.RadialZonesEnabled)
        {
            var flatHit = Math.Abs(delta) >= settings.DeltaThreshold
                && velocity >= settings.VelocityThresholdPerSecond;
            return (flatHit, settings.MaxConsecutiveSuppressions);
        }

        var (centerDelta, centerVelocity, centerMaxSuppress) = GetEffectiveCenterThresholds(settings);
        var blendT = FilterRadial.CenterToOuterBlend(
            zoneDistance,
            settings.CenterZoneEnd,
            settings.ZoneBlendWidth);

        var deltaThreshold = FilterRadial.Lerp(centerDelta, settings.OuterDeltaThreshold, blendT);
        var velocityThreshold = FilterRadial.Lerp(
            centerVelocity,
            settings.OuterVelocityThresholdPerSecond,
            blendT);
        var maxSuppressions = (int)Math.Round(FilterRadial.Lerp(
            centerMaxSuppress,
            settings.OuterMaxConsecutiveSuppressions,
            blendT));

        var hit = Math.Abs(delta) >= deltaThreshold && velocity >= velocityThreshold;
        return (hit, Math.Clamp(maxSuppressions, 1, 8));
    }

    internal static (double DeltaThreshold, double VelocityThreshold, int MaxConsecutive) GetEffectiveCenterThresholds(
        SpikeGateSettings settings)
    {
        var multiplier = Math.Clamp(settings.CenterSmoothingMultiplier, 0.25, 4.0);
        var delta = settings.CenterDeltaThreshold / multiplier;
        var velocity = settings.CenterVelocityThresholdPerSecond / multiplier;
        var maxSuppress = (int)Math.Round(settings.CenterMaxConsecutiveSuppressions * multiplier);
        return (delta, velocity, Math.Clamp(maxSuppress, 1, 8));
    }

    public void Reset(double currentValue)
    {
        _lastValue = currentValue;
        _lastRawInput = currentValue;
        _suppressedInRow = 0;
        _lastSuppressedDirection = 0;
    }
}
