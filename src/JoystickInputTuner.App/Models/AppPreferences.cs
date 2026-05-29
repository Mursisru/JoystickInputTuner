namespace JoystickInputTuner.App.Models;

public sealed class AppPreferences
{
    public bool LoggingEnabled { get; set; } = false;

    public bool ResetLogOnStartup { get; set; } = true;

    public bool ShowFilterHints { get; set; } = true;

    public bool AutoApplyInApp { get; set; } = true;

    public bool StartWithWindows { get; set; } = false;

    public string AppliedProfileName { get; set; } = string.Empty;

    /// <summary>True after the user pressed Apply (committed profile for agent / handoff).</summary>
    public bool HasAppliedSettings { get; set; }

    public string UiLanguage { get; set; } = "RU";

    public int VJoyDeviceId { get; set; } = 1;

    public string OutputSink { get; set; } = "vJoy";

    /// <summary>Set when UI exits while streaming; background agent clears after successful start.</summary>
    public bool AgentResumeStream { get; set; }

    public string LastStreamDeviceId { get; set; } = string.Empty;

    public string LastStreamAxisId { get; set; } = string.Empty;

    public int LastStreamPollingHz { get; set; } = 175;
}
