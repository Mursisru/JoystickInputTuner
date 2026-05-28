using JoystickInputTuner.Core.Models;

namespace JoystickInputTuner.Core.Output;

public sealed class MonitorSink : IOutputSink
{
    public string Name => "Monitor";

    public bool IsAvailable => true;

    public OutputSample? LastSample { get; private set; }

    public Task BeginSessionAsync(string targetAxisId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task EndSessionAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public void Publish(OutputSample sample)
    {
        LastSample = sample;
    }

    public Task PublishAsync(OutputSample sample, CancellationToken cancellationToken = default)
    {
        Publish(sample);
        return Task.CompletedTask;
    }
}
