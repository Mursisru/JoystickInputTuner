using JoystickInputTuner.Input.Models;
using SharpDX.DirectInput;

namespace JoystickInputTuner.Input;

internal sealed class BindInputPoller : IDisposable
{
    private readonly DirectInput _directInput;
    private Joystick? _joystick;
    private Keyboard? _keyboard;
    private Mouse? _mouse;

    public BindInputPoller(DirectInput directInput) => _directInput = directInput;

    public void Configure(BindLockPollConfig? config)
    {
        ReleaseDevices();

        if (config == null)
            return;

        switch (config.DeviceKind)
        {
            case BindInputDeviceKinds.Joystick:
                if (Guid.TryParse(config.DeviceId, out var guid))
                {
                    _joystick = new Joystick(_directInput, guid);
                    _joystick.Properties.BufferSize = 32;
                    _joystick.Acquire();
                }
                break;
            case BindInputDeviceKinds.Keyboard:
                _keyboard = new Keyboard(_directInput);
                _keyboard.Acquire();
                break;
            case BindInputDeviceKinds.Mouse:
                _mouse = new Mouse(_directInput);
                _mouse.Acquire();
                break;
        }
    }

    public bool IsBoundPressed(BindLockPollConfig config)
    {
        return config.DeviceKind switch
        {
            BindInputDeviceKinds.Joystick => IsJoystickButtonPressed(config.ButtonIndex),
            BindInputDeviceKinds.Keyboard => IsKeyboardKeyPressed(config.KeyCode),
            BindInputDeviceKinds.Mouse => IsMouseButtonPressed(config.ButtonIndex),
            _ => false,
        };
    }

    public InputBindCapture? CaptureFirstPress(
        IReadOnlyList<(string DeviceId, Joystick Joystick)> joysticks,
        Keyboard keyboard,
        Mouse mouse,
        Dictionary<string, bool[]> previousJoystickButtons,
        HashSet<Key> previousKeys,
        bool[] previousMouseButtons)
    {
        foreach (var (deviceId, joystick) in joysticks)
        {
            joystick.Poll();
            var current = CloneButtons(joystick.GetCurrentState());
            if (!previousJoystickButtons.TryGetValue(deviceId, out var prior))
            {
                previousJoystickButtons[deviceId] = current;
                continue;
            }

            var limit = Math.Min(prior.Length, current.Length);
            for (var b = 0; b < limit; b++)
            {
                if (current[b] && !prior[b])
                {
                    return new InputBindCapture
                    {
                        DeviceKind = BindInputDeviceKinds.Joystick,
                        DeviceId = deviceId,
                        ButtonIndex = b,
                    };
                }
            }

            previousJoystickButtons[deviceId] = current;
        }

        keyboard.Poll();
        var keys = keyboard.GetCurrentState().PressedKeys;
        var keySet = keys.Count > 0 ? keys.ToHashSet() : [];
        foreach (var key in keySet)
        {
            if (!previousKeys.Contains(key))
            {
                return new InputBindCapture
                {
                    DeviceKind = BindInputDeviceKinds.Keyboard,
                    DeviceId = BindInputDeviceKinds.SystemKeyboardId,
                    KeyCode = (int)key,
                };
            }
        }

        previousKeys.Clear();
        foreach (var key in keySet)
            previousKeys.Add(key);

        mouse.Poll();
        var mouseButtons = CloneButtons(mouse.GetCurrentState());
        var mouseLimit = Math.Min(previousMouseButtons.Length, mouseButtons.Length);
        for (var b = 0; b < mouseLimit; b++)
        {
            if (mouseButtons[b] && !previousMouseButtons[b])
            {
                return new InputBindCapture
                {
                    DeviceKind = BindInputDeviceKinds.Mouse,
                    DeviceId = BindInputDeviceKinds.SystemMouseId,
                    ButtonIndex = b,
                };
            }
        }

        Array.Copy(mouseButtons, previousMouseButtons, mouseLimit);
        return null;
    }

    private bool IsJoystickButtonPressed(int buttonIndex)
    {
        if (_joystick == null || buttonIndex < 0)
            return false;

        _joystick.Poll();
        var buttons = _joystick.GetCurrentState().Buttons;
        return buttonIndex < buttons.Length && buttons[buttonIndex];
    }

    private bool IsKeyboardKeyPressed(int keyCode)
    {
        if (_keyboard == null || keyCode < 0)
            return false;

        _keyboard.Poll();
        var key = (Key)keyCode;
        return _keyboard.GetCurrentState().PressedKeys.Contains(key);
    }

    private bool IsMouseButtonPressed(int buttonIndex)
    {
        if (_mouse == null || buttonIndex < 0)
            return false;

        _mouse.Poll();
        var buttons = _mouse.GetCurrentState().Buttons;
        return buttonIndex < buttons.Length && buttons[buttonIndex];
    }

    private static bool[] CloneButtons(JoystickState state)
    {
        var src = state.Buttons;
        if (src == null || src.Length == 0)
            return [];

        var copy = new bool[src.Length];
        Array.Copy(src, copy, src.Length);
        return copy;
    }

    private static bool[] CloneButtons(MouseState state)
    {
        var src = state.Buttons;
        if (src == null || src.Length == 0)
            return [];

        var copy = new bool[src.Length];
        Array.Copy(src, copy, src.Length);
        return copy;
    }

    private void ReleaseDevices()
    {
        if (_joystick != null)
        {
            try
            {
                _joystick.Unacquire();
                _joystick.Dispose();
            }
            catch
            {
                // ignored
            }

            _joystick = null;
        }

        if (_keyboard != null)
        {
            try
            {
                _keyboard.Unacquire();
                _keyboard.Dispose();
            }
            catch
            {
                // ignored
            }

            _keyboard = null;
        }

        if (_mouse != null)
        {
            try
            {
                _mouse.Unacquire();
                _mouse.Dispose();
            }
            catch
            {
                // ignored
            }

            _mouse = null;
        }
    }

    public void Dispose() => ReleaseDevices();
}
