namespace JoystickInputTuner.App.Models;

public sealed class AppPreferences
{
    public bool LoggingEnabled { get; set; } = false;

    public bool ResetLogOnStartup { get; set; } = true;

    public bool ShowFilterHints { get; set; } = true;

    public bool AutoApplyInApp { get; set; } = true;

    public bool StartWithWindows { get; set; } = false;

    public string AppliedProfileName { get; set; } = "Default";

    public string UiLanguage { get; set; } = "RU";

    public int VJoyDeviceId { get; set; } = 1;

    public string OutputSink { get; set; } = "vJoy";
}
