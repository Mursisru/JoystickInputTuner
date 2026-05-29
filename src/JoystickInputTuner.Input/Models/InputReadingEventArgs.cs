namespace JoystickInputTuner.Input.Models;

public sealed class InputReadingEventArgs : EventArgs
{
    public required string DeviceId { get; init; }

    public required string AxisId { get; init; }

    public required double RawValue { get; init; }

    public IReadOnlyDictionary<string, double>? AllAxes { get; init; }

    /// <summary>Joystick buttons from the stream device poll (index = button number).</summary>
    public IReadOnlyList<bool>? Buttons { get; init; }

    /// <summary>Whether the configured bind (joystick button / key / mouse button) is pressed.</summary>
    public bool BindLockPressed { get; init; }

    public required DateTimeOffset Timestamp { get; init; }
}
