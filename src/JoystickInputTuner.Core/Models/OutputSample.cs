namespace JoystickInputTuner.Core.Models;

public sealed record OutputSample(
    double RawValue,
    double FilteredValue,
    DateTimeOffset Timestamp,
    long Sequence,
    bool SpikeSuppressed,
    string TargetAxisId = "",
    IReadOnlyDictionary<string, double>? AllAxes = null);
