using Avala.Contests.Contracts;
using Stateless;

namespace Avala.Contests.Contesting;

internal enum ContestTrigger
{
    Judge,
    Rejudge,
    Decide,
    FindNone,
    Reopen,
    Close,
    Cancel,
}

internal static class ContestLifecycle
{
    public static StateMachine<ContestStatus, ContestTrigger> Create(Func<ContestStatus> read, Action<ContestStatus> write)
    {
        var machine = new StateMachine<ContestStatus, ContestTrigger>(read, write);

        machine.Configure(ContestStatus.Running)
            .Permit(ContestTrigger.Judge, ContestStatus.Judging)
            .PermitReentry(ContestTrigger.Reopen)
            .Permit(ContestTrigger.Close, ContestStatus.Closed)
            .Permit(ContestTrigger.Cancel, ContestStatus.Cancelled);

        machine.Configure(ContestStatus.Judging)
            .Permit(ContestTrigger.Decide, ContestStatus.Decided)
            .Permit(ContestTrigger.FindNone, ContestStatus.Undecided)
            .PermitReentry(ContestTrigger.Rejudge)
            .Permit(ContestTrigger.Reopen, ContestStatus.Running)
            .Permit(ContestTrigger.Close, ContestStatus.Closed)
            .Permit(ContestTrigger.Cancel, ContestStatus.Cancelled);

        foreach (var judged in new[] { ContestStatus.Decided, ContestStatus.Undecided })
        {
            machine.Configure(judged)
                .Permit(ContestTrigger.Rejudge, ContestStatus.Judging)
                .Permit(ContestTrigger.Reopen, ContestStatus.Running)
                .Permit(ContestTrigger.Close, ContestStatus.Closed)
                .Permit(ContestTrigger.Cancel, ContestStatus.Cancelled);
        }

        return machine;
    }
}
