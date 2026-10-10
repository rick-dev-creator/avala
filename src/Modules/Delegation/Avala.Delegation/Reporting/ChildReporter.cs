using Avala.Agents.Contracts.Sessions;
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

    public void Wait(DelegationRecord delegation, CallRef call, Option<ToolResult> asking, CancellationToken cancellationToken)
    {
        foreach (var child in delegation.Child.Match<JobId[]>(found => [found], () => []))
        {
            Post(child, token => WaitAsync(child, call, asking, token), cancellationToken);
        }
    }

    public void Discard(JobId child, CancellationToken cancellationToken) =>
        Post(child, async token => _ = await jobs.DiscardAsync(child, token), cancellationToken);

    public ValueTask DisposeAsync() => work.DisposeAsync();

    private void Post(JobId child, Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        _ = work.RunAsync(token => LoggedAsync(child, operation, token), cancellationToken);

    private async Task SettleAsync(DelegationRecord delegation, JobId child, Settlement settlement, Option<string> summary, CancellationToken cancellationToken)
    {
        var settled = new ChildReport(child, OutcomeOf(settlement), settlement.Status, journal.Now) { Hold = settlement.Hold };
        var reviewed = settlement.Status == JobStatus.AwaitingReview;
        var integrated = !reviewed ? settled
            : delegation.ReadOnly ? settled with { Outcome = ChildOutcome.Reported }
            : await IntegrateAsync(settled, cancellationToken);
        var gathered = await evidence.GatherAsync(integrated, summary, cancellationToken);
        var report = reviewed && delegation.ReadOnly && (await jobs.DiscardAsync(child, cancellationToken)).IsSuccess
            ? gathered with { Status = JobStatus.Discarded }
            : gathered;

        var connection = delegation.Connection.IsSome ? delegation.Connection : await evidence.ConnectionOfAsync(child, cancellationToken);

        var reported = await journal.ReportedAsync(delegation with { Report = report, Connection = connection }, report, cancellationToken);

        foreach (var parent in reported.Answered.IsNone && !journal.Calls.Expects(child) ? reported.Parent.Match<JobId[]>(found => [found], () => []) : [])
        {
            if ((await jobs.SteerAsync(parent, ToolAnswers.Briefing([reported]), cancellationToken)).IsSuccess)
            {
                _ = await journal.BriefedAsync([reported], cancellationToken);
            }
        }
    }

    private async Task WaitAsync(JobId child, CallRef call, Option<ToolResult> asking, CancellationToken cancellationToken)
    {
        var reported = journal.OfChild(child).Bind(record => record.Report.Map(report => (Record: record, Report: report)));

        await reported.Match(
            found => found.Record.Answered.IsNone
                ? journal.DeliveredAsync(found.Record, found.Report, call, cancellationToken)
                : ReturnedAsync(call, ToolAnswers.AlreadyTold(call.Item, found.Report), cancellationToken),
            () => asking.Match(result => ReturnedAsync(call, result, cancellationToken), () => OpenedAsync(child, call)));
    }

    private Task OpenedAsync(JobId child, CallRef call)
    {
        journal.Calls.Open(child, call);

        return Task.CompletedTask;
    }

    private async Task ReturnedAsync(CallRef call, ToolResult result, CancellationToken cancellationToken) => _ = await journal.ReturnAsync(call, result, cancellationToken);

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
