using Avala.Autopilot.Approving;
using Avala.Autopilot.Sourcing;
using Avala.Jobs.Contracts;

namespace Avala.Autopilot.Looping;

internal sealed class LoopSteps(IJobs jobs, TaskSources sources, AutoApprover approver, LoopGauges gauges)
{
    public IJobs Jobs => jobs;

    public TaskSources Sources => sources;

    public AutoApprover Approver => approver;

    public LoopGauges Gauges => gauges;
}
