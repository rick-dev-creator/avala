using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Records;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Escalating;

internal enum ParentReach
{
    Told,
    Woken,
    Later,
    Gone,
}

internal sealed class ParentWaking(IJobs jobs, IJobMessages messages, IJobCatalog catalog)
{
    private static readonly HoldReason[] KeptForThePerson = [HoldReason.BudgetExceeded, HoldReason.LimitNearlyReached, HoldReason.InvalidBudget, HoldReason.MemoryExceeded, HoldReason.Stopped, HoldReason.Interrupted, HoldReason.CouldNotStart];

    public async Task<ParentReach> ReachAsync(JobId parent, JobId child, string note, CancellationToken cancellationToken)
    {
        if (await TellAsync(parent, note, cancellationToken))
        {
            return ParentReach.Told;
        }

        var summary = (await catalog.HistoryAsync(parent, cancellationToken)).Map(history => history.Summary);

        return await summary.Match(found => WakeAsync(found, child, note, cancellationToken), () => GoneAsync(child, cancellationToken));
    }

    public async Task<bool> TellAsync(JobId parent, string note, CancellationToken cancellationToken) =>
        (await jobs.NoteAsync(parent, note, cancellationToken)).IsSuccess;

    public async Task<ParentReach> WakeIdleAsync(JobId parent, string note, CancellationToken cancellationToken) =>
        await (await catalog.HistoryAsync(parent, cancellationToken)).Match(
            history => WakeIdleAsync(history.Summary, note, cancellationToken),
            () => Task.FromResult(ParentReach.Later));

    private async Task<ParentReach> WakeIdleAsync(JobSummary parent, string note, CancellationToken cancellationToken) =>
        parent.Status switch
        {
            JobStatus.NeedsHelp when !parent.Hold.Match(KeptForThePerson.Contains, () => false) => Reached((await jobs.ContinueAsync(parent.Job, Avalas(note), cancellationToken)).IsSuccess),
            JobStatus.AwaitingReview => Reached((await messages.SendBackAsync(parent.Job, Avalas(note), cancellationToken)).IsSuccess),
            _ => ParentReach.Later,
        };

    private async Task<ParentReach> WakeAsync(JobSummary parent, JobId child, string note, CancellationToken cancellationToken) =>
        parent.Status switch
        {
            JobStatus.Approved or JobStatus.Discarded or JobStatus.Failed => await GoneAsync(child, cancellationToken),
            JobStatus.Running => (await jobs.ResumeAsync(parent.Job, cancellationToken)).IsSuccess && await TellAsync(parent.Job, note, cancellationToken)
                ? ParentReach.Woken
                : ParentReach.Later,
            _ => await WakeIdleAsync(parent, note, cancellationToken),
        };

    private async Task<ParentReach> GoneAsync(JobId child, CancellationToken cancellationToken)
    {
        _ = await jobs.DiscardAsync(child, cancellationToken);

        return ParentReach.Gone;
    }

    private static ParentReach Reached(bool woken) => woken ? ParentReach.Woken : ParentReach.Later;

    private static JobMessage Avalas(string note) => new(note) { Author = MessageAuthor.Avala, CausedBy = MessageAuthor.Avala };
}
