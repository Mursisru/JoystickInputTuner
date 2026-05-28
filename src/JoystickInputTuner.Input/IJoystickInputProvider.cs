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

    void Start(string deviceId, string axisId, int pollingHz);

    void Stop();
}
