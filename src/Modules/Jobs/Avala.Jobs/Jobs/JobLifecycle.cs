using Stateless;

namespace Avala.Jobs.Jobs;

internal static class JobLifecycle
{
    public static StateMachine<JobState, JobTrigger> Create(
        Func<JobState> read,
        Action<JobState> write,
        Func<bool> hasRetriesLeft)
    {
        var machine = new StateMachine<JobState, JobTrigger>(read, write);

        machine.Configure(JobState.Open)
            .Permit(JobTrigger.Discard, JobState.Discarded);

        machine.Configure(JobState.Active)
            .SubstateOf(JobState.Open)
            .Permit(JobTrigger.Fail, JobState.Failed);

        machine.Configure(JobState.Draft)
            .SubstateOf(JobState.Open)
            .Permit(JobTrigger.Submit, JobState.Preparing);

        machine.Configure(JobState.Preparing)
            .SubstateOf(JobState.Active)
            .Permit(JobTrigger.Start, JobState.Running);

        machine.Configure(JobState.Running)
            .SubstateOf(JobState.Active)
            .Permit(JobTrigger.CompleteTurn, JobState.Checking)
            .Permit(JobTrigger.Hold, JobState.NeedsHelp)
            .Permit(JobTrigger.HoldOverBudget, JobState.NeedsHelp)
            .PermitReentry(JobTrigger.Recover);

        machine.Configure(JobState.Checking)
            .SubstateOf(JobState.Active)
            .PermitReentry(JobTrigger.Recheck)
            .Permit(JobTrigger.Pass, JobState.AwaitingReview)
            .Permit(JobTrigger.HoldOverBudget, JobState.NeedsHelp)
            .PermitIf(JobTrigger.Retry, JobState.Running, hasRetriesLeft, "retries left")
            .PermitIf(JobTrigger.RequestHelp, JobState.NeedsHelp, () => !hasRetriesLeft(), "budget exhausted")
            .PermitIf(JobTrigger.HandOff, JobState.Running, hasRetriesLeft, "retries left");

        machine.Configure(JobState.NeedsHelp)
            .SubstateOf(JobState.Open)
            .Permit(JobTrigger.Hint, JobState.Running)
            .Permit(JobTrigger.HandOff, JobState.Running);

        machine.Configure(JobState.AwaitingReview)
            .SubstateOf(JobState.Open)
            .Permit(JobTrigger.SendBack, JobState.Running)
            .Permit(JobTrigger.Approve, JobState.Approved)
            .Permit(JobTrigger.HoldOverBudget, JobState.NeedsHelp);

        machine.Configure(JobState.Approved)
            .Permit(JobTrigger.Reopen, JobState.Running);

        return machine;
    }
}
