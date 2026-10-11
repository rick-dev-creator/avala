using Avala.Autopilot.Approving;
using Avala.Autopilot.Contracts;
using Avala.Autopilot.Loops;
using Avala.Autopilot.Sourcing;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Autopilot.Looping;

internal sealed class LoopSteps(IJobs jobs, TaskSources sources, AutoApprover approver, LoopGauges gauges)
{
    public IJobs Jobs => jobs;

    public TaskSources Sources => sources;

    public AutoApprover Approver => approver;

    public LoopGauges Gauges => gauges;
}

internal interface ILoopLimits
{
    Task<Result<LoopLimits, AutopilotError>> OfRepositoryAsync(string repository, CancellationToken cancellationToken);
}

internal interface ILoopStore
{
    Task<IReadOnlyList<LoopRecord>> LoopsAsync(CancellationToken cancellationToken);

    Task KeepAsync(LoopRecord loop, CancellationToken cancellationToken);
}
