namespace JoystickInputTuner.Core.Models;

public sealed class TunerProfile
{
    public string Name { get; set; } = "Default";

    public string DeviceId { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public string AxisName { get; set; } = "Yaw";

    public int PollingHz { get; set; } = 175;

    public CalibrationSettings Calibration { get; set; } = new();

    public FilterSettings Filters { get; set; } = new();

    public string OutputSink { get; set; } = "vJoy";

    public int VJoyDeviceId { get; set; } = 1;
}
