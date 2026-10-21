using Avala.Agents.Contracts.Events;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Policy;

internal enum Asking
{
    Nothing,
    Permission,
    Answer,
}

internal static class Lifecycle
{
    public static SubagentState Of(JobStatus status, Option<HoldReason> hold, Asking asking) =>
        asking != Asking.Nothing && status is JobStatus.Running or JobStatus.NeedsHelp ? SubagentState.Waiting : status switch
        {
            JobStatus.AwaitingReview or JobStatus.Approved => SubagentState.Completed,
            JobStatus.Failed => SubagentState.Failed,
            JobStatus.Discarded => SubagentState.Stopped,
            JobStatus.NeedsHelp when hold == Option<HoldReason>.Some(HoldReason.SessionLost) => SubagentState.Lost,
            JobStatus.NeedsHelp => SubagentState.Waiting,
            _ => SubagentState.Working,
        };

    public static SubagentState Of(Option<ItemOutcome> outcome, Asking asking) =>
        outcome.Match(
            ended => ended switch
            {
                ItemOutcome.Succeeded => SubagentState.Completed,
                ItemOutcome.Cancelled or ItemOutcome.Expired => SubagentState.Stopped,
                ItemOutcome.Abandoned => SubagentState.Lost,
                _ => SubagentState.Failed,
            },
            () => asking == Asking.Nothing ? SubagentState.Working : SubagentState.Waiting);

    public static string Status(SubagentState state, Asking asking, Option<HoldReason> hold) =>
        state != SubagentState.Waiting ? Camel(state.ToString())
        : asking == Asking.Permission ? "awaitingPermission"
        : asking == Asking.Answer ? "awaitingAnswer"
        : hold.Match(reason => Camel(reason.ToString()), () => Camel(state.ToString()));

    private static string Camel(string name) => $"{char.ToLowerInvariant(name[0])}{name[1..]}";
}
