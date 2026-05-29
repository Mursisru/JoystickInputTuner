namespace JoystickInputTuner.App.Models;

/// <summary>Settings tab state stored in filters.json session alongside filters.</summary>
public sealed class SessionUiPreferences
{
    public bool LoggingEnabled { get; set; }

    public bool ResetLogOnStartup { get; set; } = true;

    public bool ShowFilterHints { get; set; } = true;

    public bool AutoApplyInApp { get; set; } = true;

    public bool StartWithWindows { get; set; }

    public string UiLanguage { get; set; } = "RU";
}
