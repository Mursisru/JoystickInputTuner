namespace JoystickInputTuner.Input.Models;

/// <summary>Joystick, Keyboard, or Mouse — see <see cref="BindInputDeviceKinds"/>.</summary>
public static class BindInputDeviceKinds
{
    public const string Joystick = "Joystick";
    public const string Keyboard = "Keyboard";
    public const string Mouse = "Mouse";

    public const string SystemKeyboardId = "SYSTEM-KEYBOARD";
    public const string SystemMouseId = "SYSTEM-MOUSE";
}

public sealed class InputBindCapture
{
    public required string DeviceKind { get; init; }

    public required string DeviceId { get; init; }

    /// <summary>Joystick / mouse button index (0-based). -1 for keyboard.</summary>
    public int ButtonIndex { get; init; } = -1;

    /// <summary>SharpDX DirectInput <c>Key</c> enum value. -1 for joystick/mouse.</summary>
    public int KeyCode { get; init; } = -1;
}

public sealed class BindLockPollConfig
{
    public required string DeviceKind { get; init; }

    public required string DeviceId { get; init; }

    public int ButtonIndex { get; init; } = -1;

    public int KeyCode { get; init; } = -1;
}
