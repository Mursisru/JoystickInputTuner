using System.Collections.Concurrent;
using JoystickInputTuner.Input.Models;
using SharpDX.DirectInput;

namespace JoystickInputTuner.Input;

public sealed class DirectInputProvider : IJoystickInputProvider
{
    private static readonly InputAxisInfo[] KnownAxes =
    [
        new() { Id = "X", DisplayName = "X" },
        new() { Id = "Y", DisplayName = "Y" },
        new() { Id = "Z", DisplayName = "Z" },
        new() { Id = "RX", DisplayName = "Rotation X" },
        new() { Id = "RY", DisplayName = "Rotation Y" },
        new() { Id = "RZ", DisplayName = "Rotation Z (Yaw common)" },
        new() { Id = "SL0", DisplayName = "Slider 0" },
        new() { Id = "SL1", DisplayName = "Slider 1" },
    ];

    private readonly DirectInput _directInput = new();
    private readonly BindInputPoller _bindInputPoller;
    private readonly ConcurrentDictionary<string, Guid> _deviceMap = new();
    private readonly object _sync = new();

    private Joystick? _activeJoystick;
    private string? _activeDeviceId;
    private string? _activeAxisId;
    private BindLockPollConfig? _bindLockConfig;
    private Thread? _pollThread;
    private CancellationTokenSource? _pollCts;

    public DirectInputProvider() => _bindInputPoller = new BindInputPoller(_directInput);

    public event EventHandler<InputReadingEventArgs>? ReadingAvailable;

    public Task<IReadOnlyList<InputDeviceInfo>> GetDevicesAsync()
    {
        var devices = _directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);
        var result = new List<InputDeviceInfo>(devices.Count);
        _deviceMap.Clear();
        foreach (var device in devices)
        {
            var id = device.InstanceGuid.ToString();
            _deviceMap[id] = device.InstanceGuid;
            result.Add(new InputDeviceInfo
            {
                Id = id,
                DisplayName = string.IsNullOrWhiteSpace(device.InstanceName)
                    ? device.ProductName
                    : $"{device.InstanceName} ({device.ProductName})"
            });
        }

        return Task.FromResult<IReadOnlyList<InputDeviceInfo>>(result);
    }

    public Task<IReadOnlyList<InputAxisInfo>> GetAxesAsync(string deviceId)
    {
        return Task.FromResult<IReadOnlyList<InputAxisInfo>>(KnownAxes);
    }

    public async Task<InputAxisInfo?> DetectMostActiveAxisAsync(
        string deviceId,
        int sampleDurationMs,
        int pollingHz,
        CancellationToken cancellationToken = default)
    {
        if (!_deviceMap.TryGetValue(deviceId, out var guid))
            return null;

        var periodMs = Math.Clamp(1000 / Math.Max(20, pollingHz), 2, 50);
        var samples = Math.Max(20, sampleDurationMs / periodMs);
        using var joystick = new Joystick(_directInput, guid);
        joystick.Acquire();

        joystick.Poll();
        var baseState = joystick.GetCurrentState();
        var baseline = ReadAllAxes(baseState);
        var maxDeltaByAxis = baseline.ToDictionary(static kv => kv.Key, static _ => 0.0);

        for (var i = 0; i < samples; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            joystick.Poll();
            var state = joystick.GetCurrentState();
            var now = ReadAllAxes(state);
            foreach (var axis in KnownAxes)
            {
                if (!now.TryGetValue(axis.Id, out var value))
                    continue;

                var delta = Math.Abs(value - baseline[axis.Id]);
                if (delta > maxDeltaByAxis[axis.Id])
                    maxDeltaByAxis[axis.Id] = delta;
            }

            await Task.Delay(periodMs, cancellationToken).ConfigureAwait(false);
        }

        var winner = KnownAxes
            .Select(axis => new { Axis = axis, Delta = maxDeltaByAxis[axis.Id] })
            .OrderByDescending(x => x.Delta)
            .FirstOrDefault();

        return winner is { Delta: > 0.02 } ? winner.Axis : null;
    }

    public async Task<int?> CaptureNextButtonPressAsync(
        string deviceId,
        int sampleDurationMs,
        int pollingHz,
        CancellationToken cancellationToken = default)
    {
        if (!_deviceMap.TryGetValue(deviceId, out var guid))
            return null;

        var periodMs = Math.Clamp(1000 / Math.Max(20, pollingHz), 2, 50);
        var samples = Math.Max(20, sampleDurationMs / periodMs);
        using var joystick = new Joystick(_directInput, guid);
        joystick.Acquire();

        bool[]? previous = null;
        for (var i = 0; i < samples; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            joystick.Poll();
            var current = CloneButtons(joystick.GetCurrentState());
            if (previous != null)
            {
                var limit = Math.Min(previous.Length, current.Length);
                for (var b = 0; b < limit; b++)
                {
                    if (current[b] && !previous[b])
                        return b;
                }
            }

            previous = current;
            await Task.Delay(periodMs, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    public async Task<InputBindCapture?> CaptureNextInputBindAsync(
        int sampleDurationMs,
        int pollingHz,
        CancellationToken cancellationToken = default)
    {
        await GetDevicesAsync().ConfigureAwait(false);

        var periodMs = Math.Clamp(1000 / Math.Max(20, pollingHz), 2, 50);
        var samples = Math.Max(20, sampleDurationMs / periodMs);
        var joysticks = new List<(string DeviceId, Joystick Joystick)>();

        using var keyboard = new Keyboard(_directInput);
        using var mouse = new Mouse(_directInput);
        keyboard.Acquire();
        mouse.Acquire();

        try
        {
            foreach (var pair in _deviceMap)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var joystick = new Joystick(_directInput, pair.Value);
                    joystick.Acquire();
                    joysticks.Add((pair.Key, joystick));
                }
                catch
                {
                    // Device may disappear between enumeration and open.
                }
            }

            var previousJoystickButtons = new Dictionary<string, bool[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var (deviceId, joystick) in joysticks)
            {
                joystick.Poll();
                previousJoystickButtons[deviceId] = CloneButtons(joystick.GetCurrentState());
            }

            var previousKeys = new HashSet<Key>();
            keyboard.Poll();
            foreach (var key in keyboard.GetCurrentState().PressedKeys)
                previousKeys.Add(key);

            var previousMouseButtons = new bool[8];
            mouse.Poll();
            var initialMouse = CloneMouseButtons(mouse.GetCurrentState());
            Array.Copy(initialMouse, previousMouseButtons, Math.Min(initialMouse.Length, previousMouseButtons.Length));

            var scanner = new BindInputPoller(_directInput);
            for (var i = 0; i < samples; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var capture = scanner.CaptureFirstPress(
                    joysticks,
                    keyboard,
                    mouse,
                    previousJoystickButtons,
                    previousKeys,
                    previousMouseButtons);
                if (capture != null)
                    return capture;

                await Task.Delay(periodMs, cancellationToken).ConfigureAwait(false);
            }

            return null;
        }
        finally
        {
            foreach (var (_, joystick) in joysticks)
            {
                try
                {
                    joystick.Unacquire();
                    joystick.Dispose();
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }
        }
    }

    public void Start(string deviceId, string axisId, int pollingHz, BindLockPollConfig? bindLockConfig = null)
    {
        lock (_sync)
        {
            StopInternal();

            if (!_deviceMap.TryGetValue(deviceId, out var guid))
                throw new InvalidOperationException("Device is not available.");

            var joystick = new Joystick(_directInput, guid);
            joystick.Properties.BufferSize = 32;
            joystick.Acquire();

            _activeJoystick = joystick;
            _activeDeviceId = deviceId;
            _activeAxisId = axisId;
            _bindLockConfig = bindLockConfig;
            _bindInputPoller.Configure(ShouldUseDedicatedBindPoller(bindLockConfig, deviceId) ? bindLockConfig : null);

            var periodMs = Math.Clamp(1000 / Math.Max(20, pollingHz), 2, 50);
            _pollCts = new CancellationTokenSource();
            var token = _pollCts.Token;
            var bindConfig = _bindLockConfig;
            _pollThread = new Thread(() => PollLoop(joystick, deviceId, axisId, bindConfig, periodMs, token))
            {
                IsBackground = true,
                Name = "DirectInputPoll",
                Priority = ThreadPriority.AboveNormal
            };
            _pollThread.Start();
        }
    }

    public void Stop()
    {
        lock (_sync)
            StopInternal();
    }

    private void StopInternal()
    {
        var cts = _pollCts;
        _pollCts = null;
        if (cts != null)
        {
            cts.Cancel();
            cts.Dispose();
        }

        var thread = _pollThread;
        _pollThread = null;
        if (thread != null && thread.IsAlive)
            thread.Join(TimeSpan.FromMilliseconds(500));

        if (_activeJoystick != null)
        {
            _activeJoystick.Unacquire();
            _activeJoystick.Dispose();
            _activeJoystick = null;
        }

        _bindInputPoller.Configure(null);
        _bindLockConfig = null;

        _activeDeviceId = null;
        _activeAxisId = null;
    }

    private static bool ShouldUseDedicatedBindPoller(BindLockPollConfig? config, string streamDeviceId)
    {
        if (config == null)
            return false;

        return !string.Equals(config.DeviceKind, BindInputDeviceKinds.Joystick, StringComparison.OrdinalIgnoreCase) ||
               !config.DeviceId.Equals(streamDeviceId, StringComparison.OrdinalIgnoreCase);
    }

    private bool EvaluateBindPressed(JoystickState streamState, BindLockPollConfig? config)
    {
        if (config == null)
            return false;

        if (string.Equals(config.DeviceKind, BindInputDeviceKinds.Joystick, StringComparison.OrdinalIgnoreCase) &&
            config.DeviceId.Equals(_activeDeviceId, StringComparison.OrdinalIgnoreCase))
        {
            var buttons = streamState.Buttons;
            return config.ButtonIndex >= 0 &&
                   config.ButtonIndex < buttons.Length &&
                   buttons[config.ButtonIndex];
        }

        return _bindInputPoller.IsBoundPressed(config);
    }

    private void PollLoop(
        Joystick joystick,
        string deviceId,
        string axisId,
        BindLockPollConfig? bindLockConfig,
        int periodMs,
        CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                joystick.Poll();
                var state = joystick.GetCurrentState();
                var buttons = CloneButtons(state);
                var bindPressed = EvaluateBindPressed(state, bindLockConfig);

                var allRawAxes = ReadAllAxes(state);
                var allNormalizedAxes = new Dictionary<string, double>(allRawAxes.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var pair in allRawAxes)
                    allNormalizedAxes[pair.Key] = NormalizeAxis(pair.Value);

                var normalized = allNormalizedAxes.TryGetValue(axisId, out var selected)
                    ? selected
                    : NormalizeAxis(ReadAxis(state, axisId));

                ReadingAvailable?.Invoke(this, new InputReadingEventArgs
                {
                    DeviceId = deviceId,
                    AxisId = axisId,
                    RawValue = normalized,
                    AllAxes = allNormalizedAxes,
                    Buttons = buttons,
                    BindLockPressed = bindPressed,
                    Timestamp = DateTimeOffset.UtcNow
                });
            }
            catch
            {
                // Polling failures are expected during unplug/replug.
            }

            if (token.WaitHandle.WaitOne(periodMs))
                break;
        }
    }

    private static int ReadAxis(JoystickState state, string axisId)
    {
        return axisId switch
        {
            "X" => state.X,
            "Y" => state.Y,
            "Z" => state.Z,
            "RX" => state.RotationX,
            "RY" => state.RotationY,
            "RZ" => state.RotationZ,
            "SL0" => state.Sliders.Length > 0 ? state.Sliders[0] : 0,
            "SL1" => state.Sliders.Length > 1 ? state.Sliders[1] : 0,
            _ => state.RotationZ
        };
    }

    private static Dictionary<string, int> ReadAllAxes(JoystickState state)
    {
        return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["X"] = state.X,
            ["Y"] = state.Y,
            ["Z"] = state.Z,
            ["RX"] = state.RotationX,
            ["RY"] = state.RotationY,
            ["RZ"] = state.RotationZ,
            ["SL0"] = state.Sliders.Length > 0 ? state.Sliders[0] : 0,
            ["SL1"] = state.Sliders.Length > 1 ? state.Sliders[1] : 0,
        };
    }

    private static double NormalizeAxis(int value)
    {
        // DirectInput axis is usually 0..65535.
        const double min = 0.0;
        const double max = 65535.0;
        var normalized = ((value - min) / (max - min)) * 2.0 - 1.0;
        return Math.Clamp(normalized, -1.0, 1.0);
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

    private static bool[] CloneMouseButtons(MouseState state)
    {
        var src = state.Buttons;
        if (src == null || src.Length == 0)
            return [];

        var copy = new bool[src.Length];
        Array.Copy(src, copy, src.Length);
        return copy;
    }

    public void Dispose()
    {
        Stop();
        _bindInputPoller.Dispose();
        _directInput.Dispose();
    }
}
