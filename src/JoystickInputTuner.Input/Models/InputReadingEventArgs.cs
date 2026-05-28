namespace JoystickInputTuner.Input.Models;

public sealed class InputReadingEventArgs : EventArgs
{
    public required string DeviceId { get; init; }

    public required string AxisId { get; init; }

    public required double RawValue { get; init; }

    public IReadOnlyDictionary<string, double>? AllAxes { get; init; }

    public required DateTimeOffset Timestamp { get; init; }
}
