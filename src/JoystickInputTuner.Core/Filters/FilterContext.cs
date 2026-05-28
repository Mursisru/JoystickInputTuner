using JoystickInputTuner.Core.Models;

namespace JoystickInputTuner.Core.Filters;

public sealed class FilterContext
{
    public required FilterSettings Settings { get; init; }

    public required FilterStatistics Statistics { get; init; }

    public required TimeSpan DeltaTime { get; init; }

    /// <summary>Normalized axis value before the filter chain (for release/catch-up logic).</summary>
    public required double RawInput { get; init; }

    /// <summary>Set by host when target-axis intent is detected (bypass parasitic guards).</summary>
    public bool TargetAxisIntentActive { get; init; }

    /// <summary>Cross-axis shield block strength (0..1) for stricter rate limiting while active.</summary>
    public double CrossAxisBlockStrength { get; init; }
}
