using JoystickInputTuner.Core.Models;

namespace JoystickInputTuner.App.Models;

public sealed class FilterSessionState
{
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;

    public string ProfileName { get; set; } = "Default";

    /// <summary>True after the user changed filters in the UI (not profile seed / programmatic load).</summary>
    public bool UserModified { get; set; }

    public FilterSettings Filters { get; set; } = new();
}
