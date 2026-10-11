using Avala.Sdk;
using Avala.Triggers.Firing;

namespace Avala.Triggers.Looping;

internal sealed class QueuedLoops(TriggerDispatch dispatch) : IStartupTask
{
    public StartupStage Stage => StartupStage.Recovery;

    public Task RunAsync(CancellationToken cancellationToken) => dispatch.ResumeAsync(cancellationToken);
}
