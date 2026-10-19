using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Contracts;

public enum SubagentState
{
    Working,
    Waiting,
    Completed,
    Failed,
    Stopped,
    Lost,
}

public static class SubagentLifecycle
{
    public static SubagentState Of(JobStatus status, Option<HoldReason> hold, bool asking) =>
        asking && status is JobStatus.Running or JobStatus.NeedsHelp ? SubagentState.Waiting : Of(status, hold);

    public static SubagentState Of(JobStatus status, Option<HoldReason> hold) => status switch
    {
        JobStatus.AwaitingReview or JobStatus.Approved => SubagentState.Completed,
        JobStatus.Failed => SubagentState.Failed,
        JobStatus.Discarded => SubagentState.Stopped,
        JobStatus.NeedsHelp when hold == Option<HoldReason>.Some(HoldReason.SessionLost) => SubagentState.Lost,
        JobStatus.NeedsHelp => SubagentState.Waiting,
        _ => SubagentState.Working,
    };
}
