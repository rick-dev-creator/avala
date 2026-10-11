using Avala.Sdk;
using Avala.Triggers.Firing;

namespace Avala.Triggers.Looping;

internal sealed class QueuedLoops(TrippedLoops tripped, TriggerDispatch dispatch) : IStartupTask
{
    public StartupStage Stage => StartupStage.Recovery;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await tripped.SweepAsync(cancellationToken);
        await dispatch.ResumeAsync(cancellationToken);
    }
}
