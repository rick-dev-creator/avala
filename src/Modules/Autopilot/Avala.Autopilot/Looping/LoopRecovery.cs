using Avala.Sdk;

namespace Avala.Autopilot.Looping;

internal sealed class LoopRestore(LoopRegistry registry) : IStartupTask
{
    public Task RunAsync(CancellationToken cancellationToken) => registry.RestoreAsync(cancellationToken);
}

internal sealed class LoopRecovery(LoopRegistry registry) : IStartupTask
{
    public StartupStage Stage => StartupStage.Recovery;

    public Task RunAsync(CancellationToken cancellationToken)
    {
        registry.Recover();

        return Task.CompletedTask;
    }
}
