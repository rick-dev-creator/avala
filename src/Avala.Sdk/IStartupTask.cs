using Avala.Sdk.Events;

namespace Avala.Sdk;

public interface IStartupTask
{
    Task RunAsync(CancellationToken cancellationToken);
}

public sealed record StartupCompleted : IIntegrationEvent;
