using Stateless;

namespace Avala.Jobs.Jobs;

internal static class JobLifecycle
{
    public static StateMachine<JobState, JobTrigger> Create(
        Func<JobState> read,
        Action<JobState> write,
        Func<bool> hasRetriesLeft,
        Func<bool> started)
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
            .Permit(JobTrigger.Start, JobState.Running)
            .Permit(JobTrigger.HoldUnstarted, JobState.NeedsHelp);

        machine.Configure(JobState.Running)
            .SubstateOf(JobState.Active)
            .Permit(JobTrigger.CompleteTurn, JobState.Checking)
            .Permit(JobTrigger.Hold, JobState.NeedsHelp)
            .Permit(JobTrigger.HoldOverBudget, JobState.NeedsHelp)
            .Permit(JobTrigger.HoldUnstarted, JobState.NeedsHelp)
            .PermitReentry(JobTrigger.Recover);

        machine.Configure(JobState.Checking)
            .SubstateOf(JobState.Active)
            .PermitReentry(JobTrigger.Recheck)
            .Permit(JobTrigger.Pass, JobState.AwaitingReview)
            .Permit(JobTrigger.HoldOverBudget, JobState.NeedsHelp)
            .Permit(JobTrigger.HoldUnverifiable, JobState.NeedsHelp)
            .PermitIf(JobTrigger.Retry, JobState.Running, hasRetriesLeft, "retries left")
            .PermitIf(JobTrigger.RequestHelp, JobState.NeedsHelp, () => !hasRetriesLeft(), "budget exhausted")
            .PermitIf(JobTrigger.HandOff, JobState.Running, hasRetriesLeft, "retries left")
            .Permit(JobTrigger.Relay, JobState.Running)
            .Permit(JobTrigger.HoldAtLimit, JobState.NeedsHelp);

        machine.Configure(JobState.NeedsHelp)
            .SubstateOf(JobState.Open)
            .PermitIf(JobTrigger.Hint, JobState.Running, started, "started")
            .PermitIf(JobTrigger.HandOff, JobState.Running, started, "started")
            .PermitIf(JobTrigger.RetryLaunch, JobState.Preparing, () => !started(), "could not start");

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
