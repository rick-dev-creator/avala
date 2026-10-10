using Avala.Sdk.Events;

namespace Avala.Sdk;

public interface IStartupTask
{
    StartupStage Stage => StartupStage.Restore;

    Task RunAsync(CancellationToken cancellationToken);
}

public enum StartupStage
{
    Restore,
    Recovery,
}

public sealed record StartupCompleted : IIntegrationEvent;
