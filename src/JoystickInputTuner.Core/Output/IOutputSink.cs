using JoystickInputTuner.Core.Models;

namespace JoystickInputTuner.Core.Output;

public interface IOutputSink
{
    string Name { get; }

    bool IsAvailable { get; }

    Task BeginSessionAsync(string targetAxisId, CancellationToken cancellationToken = default);

    void Publish(OutputSample sample);

    Task PublishAsync(OutputSample sample, CancellationToken cancellationToken = default);

    Task EndSessionAsync(CancellationToken cancellationToken = default);
}
