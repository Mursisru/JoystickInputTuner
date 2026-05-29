using JoystickInputTuner.Input.Models;
using SharpDX.DirectInput;

namespace JoystickInputTuner.Input;

public static class BindInputFormatting
{
    public static BindLockPollConfig? CreatePollConfig(
        bool enabled,
        string deviceKind,
        string deviceId,
        int buttonIndex,
        int keyCode)
    {
        if (!enabled)
            return null;

        if (string.Equals(deviceKind, BindInputDeviceKinds.Keyboard, StringComparison.OrdinalIgnoreCase))
        {
            if (keyCode < 0)
                return null;

            return new BindLockPollConfig
            {
                DeviceKind = BindInputDeviceKinds.Keyboard,
                DeviceId = BindInputDeviceKinds.SystemKeyboardId,
                KeyCode = keyCode,
            };
        }

        if (buttonIndex < 0 || string.IsNullOrWhiteSpace(deviceId))
            return null;

        return new BindLockPollConfig
        {
            DeviceKind = string.IsNullOrWhiteSpace(deviceKind) ? BindInputDeviceKinds.Joystick : deviceKind,
            DeviceId = deviceId,
            ButtonIndex = buttonIndex,
        };
    }

    public static string GetKeyName(int keyCode)
    {
        if (keyCode < 0)
            return "?";

        var key = (Key)keyCode;
        return Enum.IsDefined(key) ? key.ToString() : $"#{keyCode}";
    }

    public static string GetMouseButtonName(int buttonIndex) => buttonIndex switch
    {
        0 => "Left",
        1 => "Right",
        2 => "Middle",
        3 => "Side1",
        4 => "Side2",
        _ => $"#{buttonIndex}",
    };
}
