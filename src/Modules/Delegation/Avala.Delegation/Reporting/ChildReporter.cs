using Avala.Delegation.Contracts;
using Avala.Delegation.Records;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Microsoft.Extensions.Logging;

namespace Avala.Delegation.Reporting;

internal sealed record Settlement(JobStatus Status, Option<HoldReason> Hold)
{
    public bool Held { get; init; }
}

internal sealed partial class ChildReporter(IJobs jobs, ChildEvidence evidence, DelegationJournal journal, ILogger<ChildReporter> logger) : IAsyncDisposable
{
    private readonly SerialExecutor work = new();

    public void Report(DelegationRecord delegation, JobId child, Settlement settlement, Option<string> summary, CancellationToken cancellationToken) =>
        Post(child, token => SettleAsync(delegation, child, settlement, summary, token), cancellationToken);

    public void Discard(JobId child, CancellationToken cancellationToken) =>
        Post(child, async token => _ = await jobs.DiscardAsync(child, token), cancellationToken);

    public ValueTask DisposeAsync() => work.DisposeAsync();

    private void Post(JobId child, Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        _ = work.RunAsync(token => LoggedAsync(child, operation, token), cancellationToken);

    private async Task SettleAsync(DelegationRecord delegation, JobId child, Settlement settlement, Option<string> summary, CancellationToken cancellationToken)
    {
        var settled = new ChildReport(child, OutcomeOf(settlement), settlement.Status, journal.Now) { Hold = settlement.Hold };
        var integrated = settlement.Status == JobStatus.AwaitingReview ? await IntegrateAsync(settled, cancellationToken) : settled;
        var report = await evidence.GatherAsync(integrated, summary, cancellationToken);

        var connection = delegation.Connection.IsSome ? delegation.Connection : await evidence.ConnectionOfAsync(child, cancellationToken);

        await journal.ReportedAsync(delegation with { Report = report, Connection = connection }, report, cancellationToken);
    }

    private async Task<ChildReport> IntegrateAsync(ChildReport settled, CancellationToken cancellationToken) =>
        await (await jobs.ApproveAsync(settled.Child, cancellationToken)).Match(
            approval => Task.FromResult(settled with { Outcome = ChildOutcome.Integrated, Status = JobStatus.Approved, Delivery = approval.Delivery }),
            async refusal => refusal == JobRejection.MergeConflict
                ? settled with { Outcome = ChildOutcome.Conflict, Refusal = refusal, Conflicts = await evidence.ConflictsAsync(settled.Child, cancellationToken) }
                : settled with { Outcome = ChildOutcome.NotIntegrated, Refusal = refusal });

    private static ChildOutcome OutcomeOf(Settlement settlement) => settlement.Status switch
    {
        JobStatus.Approved => ChildOutcome.Integrated,
        JobStatus.Failed => ChildOutcome.Failed,
        JobStatus.Discarded => ChildOutcome.Discarded,
        JobStatus.NeedsHelp when settlement.Hold.IsSome || settlement.Held => ChildOutcome.Held,
        JobStatus.NeedsHelp => ChildOutcome.RetriesExhausted,
        _ => ChildOutcome.NotIntegrated,
    };

    private async Task LoggedAsync(JobId child, Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        try
        {
            await operation(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogReportFailed(child.Value, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Reporting the delegated job {Job} to its parent failed")]
    private partial void LogReportFailed(Guid job, Exception exception);
}
