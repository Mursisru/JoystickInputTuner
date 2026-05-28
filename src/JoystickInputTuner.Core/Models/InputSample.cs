namespace JoystickInputTuner.Core.Models;

public sealed record InputSample(
    double Value,
    DateTimeOffset Timestamp,
    long Sequence);
