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
    public async Task<ParentReach> ReachAsync(JobId parent, JobId child, string note, CancellationToken cancellationToken)
    {
        if ((await jobs.NoteAsync(parent, note, cancellationToken)).IsSuccess)
        {
            return ParentReach.Told;
        }

        var status = (await catalog.HistoryAsync(parent, cancellationToken)).Map(history => history.Summary.Status);

        return await status.Match(found => WakeAsync(parent, child, found, note, cancellationToken), () => GoneAsync(child, cancellationToken));
    }

    public async Task<ParentReach> WakeIdleAsync(JobId parent, JobStatus status, string note, CancellationToken cancellationToken) =>
        status switch
        {
            JobStatus.NeedsHelp => Reached((await jobs.ContinueAsync(parent, Avalas(note), cancellationToken)).IsSuccess),
            JobStatus.AwaitingReview => Reached((await messages.SendBackAsync(parent, Avalas(note), cancellationToken)).IsSuccess),
            _ => ParentReach.Later,
        };

    private async Task<ParentReach> WakeAsync(JobId parent, JobId child, JobStatus status, string note, CancellationToken cancellationToken) =>
        status switch
        {
            JobStatus.Approved or JobStatus.Discarded or JobStatus.Failed => await GoneAsync(child, cancellationToken),
            JobStatus.Running => (await jobs.ResumeAsync(parent, cancellationToken)).IsSuccess && (await jobs.NoteAsync(parent, note, cancellationToken)).IsSuccess
                ? ParentReach.Woken
                : ParentReach.Later,
            _ => await WakeIdleAsync(parent, status, note, cancellationToken),
        };

    private async Task<ParentReach> GoneAsync(JobId child, CancellationToken cancellationToken)
    {
        _ = await jobs.DiscardAsync(child, cancellationToken);

        return ParentReach.Gone;
    }

    private static ParentReach Reached(bool woken) => woken ? ParentReach.Woken : ParentReach.Later;

    private static JobMessage Avalas(string note) => new(note) { Author = MessageAuthor.Avala, CausedBy = MessageAuthor.Avala };
}
