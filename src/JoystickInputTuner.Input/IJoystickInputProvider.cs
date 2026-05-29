using JoystickInputTuner.Input.Models;

namespace JoystickInputTuner.Input;

public interface IJoystickInputProvider : IDisposable
{
    event EventHandler<InputReadingEventArgs>? ReadingAvailable;

    Task<IReadOnlyList<InputDeviceInfo>> GetDevicesAsync();

    Task<IReadOnlyList<InputAxisInfo>> GetAxesAsync(string deviceId);

    Task<InputAxisInfo?> DetectMostActiveAxisAsync(
        string deviceId,
        int sampleDurationMs,
        int pollingHz,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the first button index that transitions to pressed, or null on timeout.</summary>
    Task<int?> CaptureNextButtonPressAsync(
        string deviceId,
        int sampleDurationMs,
        int pollingHz,
        CancellationToken cancellationToken = default);

    /// <summary>Scans joysticks, keyboard, and mouse for the first new button/key press.</summary>
    Task<InputBindCapture?> CaptureNextInputBindAsync(
        int sampleDurationMs,
        int pollingHz,
        CancellationToken cancellationToken = default);

    void Start(string deviceId, string axisId, int pollingHz, BindLockPollConfig? bindLockConfig = null);

    void Stop();
}
