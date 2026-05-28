using JoystickInputTuner.Core.Models;
using JoystickInputTuner.Core.Output;
using JoystickInputTuner.Input.VJoy;

namespace JoystickInputTuner.Input.Output;

public sealed class VJoyOutputSink : IOutputSink
{
    private const double PublishDeadband = 0.018;

    private readonly object _sync = new();
    private uint _deviceId = 1;
    private string _targetAxisId = "RZ";
    private bool _sessionActive;
    private int _setAxisFailCount;
    private double _lastPublishedValue;
    private bool _hasPublishedValue;

    public string Name => "vJoy Virtual Device";

    public bool IsAvailable => VJoyNative.IsDriverAvailable();

    public string? LastError { get; private set; }

    public void Configure(uint deviceId)
    {
        if (deviceId is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(deviceId), "vJoy device id must be between 1 and 16.");

        _deviceId = deviceId;
    }

    public static void TryReleaseStale(uint deviceId) => VJoyNative.TryReleaseStale(deviceId);

    public Task BeginSessionAsync(string targetAxisId, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            EndSessionInternal();

            if (!VJoyNative.TryAcquire(_deviceId, out var error))
            {
                LastError = error;
                throw new InvalidOperationException(error ?? "Failed to acquire vJoy device.");
            }

            _targetAxisId = string.IsNullOrWhiteSpace(targetAxisId) ? "RZ" : targetAxisId.Trim();
            _sessionActive = true;
            _setAxisFailCount = 0;
            _hasPublishedValue = false;
            LastError = null;
            VJoyNative.CenterAllKnownAxes(_deviceId);
        }

        return Task.CompletedTask;
    }

    public void Publish(OutputSample sample)
    {
        lock (_sync)
        {
            if (!_sessionActive)
                return;

            var value = sample.FilteredValue;
            if (Math.Abs(value) < 0.002)
                value = 0.0;
            if (_hasPublishedValue && Math.Abs(value - _lastPublishedValue) < PublishDeadband)
                return;

            var axisId = string.IsNullOrWhiteSpace(sample.TargetAxisId) ? _targetAxisId : sample.TargetAxisId;
            if (!RecordSetAxisResult(VJoyNative.SetAxisNormalized(_deviceId, axisId, value)))
                return;

            _lastPublishedValue = value;
            _hasPublishedValue = true;
        }
    }

    public Task PublishAsync(OutputSample sample, CancellationToken cancellationToken = default)
    {
        Publish(sample);
        return Task.CompletedTask;
    }

    public Task EndSessionAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
            EndSessionInternal();

        return Task.CompletedTask;
    }

    private void EndSessionInternal()
    {
        if (!_sessionActive)
            return;

        VJoyNative.CenterAllKnownAxes(_deviceId);
        VJoyNative.Relinquish(_deviceId);
        _sessionActive = false;
        _hasPublishedValue = false;
    }

    private bool RecordSetAxisResult(bool ok)
    {
        if (ok)
            return true;

        _setAxisFailCount++;
        if (_setAxisFailCount == 1 || _setAxisFailCount % 120 == 0)
            LastError = $"vJoy SetAxis failed ({_setAxisFailCount}x). Device #{_deviceId} may be busy or not acquired.";

        return false;
    }
}
