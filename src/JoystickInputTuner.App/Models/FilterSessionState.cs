using JoystickInputTuner.Core.Models;

namespace JoystickInputTuner.App.Models;

public sealed class FilterSessionState
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;

    public string ProfileName { get; set; } = "Default";

    /// <summary>True after the user changed settings in the UI (not profile seed / programmatic load).</summary>
    public bool UserModified { get; set; }

    public FilterSettings Filters { get; set; } = new();

    /// <summary>Input tab — last selected device.</summary>
    public string DeviceId { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Input tab — stream axis id (X, Y, RZ, …).</summary>
    public string AxisId { get; set; } = string.Empty;

    public int PollingHz { get; set; } = 175;

    public string OutputSink { get; set; } = "vJoy";

    public int VJoyDeviceId { get; set; } = 1;

    public CalibrationSettings Calibration { get; set; } = new();

    /// <summary>Monitor tab — which device raw overlay axes are visible (X, Y, Z, …).</summary>
    public string[] MonitorChartEnabledAxes { get; set; } = [];

    /// <summary>Settings tab checkboxes and language.</summary>
    public SessionUiPreferences? Ui { get; set; }
}
