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

    public Option<JobFailure> Failure { get; init; }

    public bool GivenUp { get; init; }
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

    public void Held(DelegationRecord delegation, JobId child, JobHold hold, CancellationToken cancellationToken) =>
        Post(child, token => HeldAsync(delegation, child, hold, token), cancellationToken);

    public Task<Option<JobRejection>> RetryAsync(DelegationRecord delegation, JobId child, JobHold hold, CancellationToken cancellationToken) =>
        work.RunAsync(token => RetriedAsync(delegation, child, hold, token), cancellationToken);

    public void Stop(DelegationRecord delegation, CallRef call, CancellationToken cancellationToken)
    {
        foreach (var child in delegation.Child.Match<JobId[]>(found => [found], () => []))
        {
            Post(child, token => StopAsync(child, call, token), cancellationToken);
        }
    }

    public void Discard(JobId child, CancellationToken cancellationToken) =>
        Post(child, async token => _ = await jobs.DiscardAsync(child, token), cancellationToken);

    public ValueTask DisposeAsync() => work.DisposeAsync();

    private void Post(JobId child, Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        _ = work.RunAsync(token => LoggedAsync(child, operation, token), cancellationToken);

    private async Task SettleAsync(DelegationRecord delegation, JobId child, Settlement settlement, Option<string> summary, CancellationToken cancellationToken)
    {
        var report = await ReportOfAsync(delegation, child, settlement, summary, cancellationToken);

        if (report.Outcome == ChildOutcome.NotIntegrated && report.Refusal == Option<JobRejection>.Some(JobRejection.NotAwaitingReview))
        {
            await journal.WentOnAsync(delegation, child, summary, cancellationToken);

            return;
        }
        var connection = delegation.Connection.IsSome ? delegation.Connection : await evidence.ConnectionOfAsync(child, cancellationToken);
        var reported = await journal.ReportedAsync(delegation with { Report = report, Connection = connection }, report, cancellationToken);

        if (reported.Answered.IsNone && !journal.Calls.Expects(child))
        {
            await BriefAsync(reported, cancellationToken);
        }
    }

    private async Task<ChildReport> ReportOfAsync(DelegationRecord delegation, JobId child, Settlement settlement, Option<string> summary, CancellationToken cancellationToken)
    {
        var settled = new ChildReport(child, OutcomeOf(settlement), settlement.Status, journal.Now) { Hold = settlement.Hold, Failure = settlement.Failure, Retries = delegation.Retries };

        if (settlement.GivenUp)
        {
            return await DiscardedAsync(await evidence.GatherAsync(settled with { Outcome = ChildOutcome.GivenUp }, summary, cancellationToken), cancellationToken);
        }

        if (settlement.Status != JobStatus.AwaitingReview)
        {
            return await evidence.GatherAsync(settled, summary, cancellationToken);
        }

        if (!delegation.ReadOnly)
        {
            return await evidence.GatherAsync(await IntegrateAsync(settled, cancellationToken), summary, cancellationToken);
        }

        return await DiscardedAsync(await evidence.GatherAsync(settled with { Outcome = ChildOutcome.Reported }, summary, cancellationToken), cancellationToken);
    }

    private async Task<ChildReport> DiscardedAsync(ChildReport gathered, CancellationToken cancellationToken) =>
        (await jobs.DiscardAsync(gathered.Child, cancellationToken)).IsSuccess ? gathered with { Status = JobStatus.Discarded } : gathered;

    private async Task<Option<JobRejection>> RetriedAsync(DelegationRecord delegation, JobId child, JobHold hold, CancellationToken cancellationToken)
    {
        var continued = await jobs.ContinueAsync(child, new JobMessage(ToolAnswers.Retrying(hold)) { Author = MessageAuthor.Avala, CausedBy = MessageAuthor.Avala }, cancellationToken);

        if (continued.IsSuccess)
        {
            foreach (var parent in delegation.Parent.Match<JobId[]>(found => [found], () => []))
            {
                _ = await jobs.NoteAsync(parent, ToolAnswers.RetryNote(delegation, child, hold), cancellationToken);
            }
        }

        return continued.Match(_ => Option<JobRejection>.None, refusal => refusal);
    }

    private async Task BriefAsync(DelegationRecord reported, CancellationToken cancellationToken)
    {
        foreach (var parent in reported.Parent.Match<JobId[]>(found => [found], () => []))
        {
            if ((await jobs.NoteAsync(parent, ToolAnswers.Briefing([reported]), cancellationToken)).IsSuccess)
            {
                await journal.SteeredAsync(reported, cancellationToken);
            }
        }
    }

    private async Task WaitAsync(JobId child, CallRef call, Option<ToolResult> asking, CancellationToken cancellationToken)
    {
        var reported = journal.OfChild(child).Bind(record => record.Report.Map(report => (Record: record, Report: report)));

        await reported.Match(
            found => journal.Unanswered(found.Record)
                ? journal.DeliveredAsync(found.Record, found.Report, call, cancellationToken)
                : ReturnedAsync(call, ToolAnswers.AlreadyTold(call.Item, found.Report), cancellationToken),
            () => asking.Match(result => ReturnedAsync(call, result, cancellationToken), () => OpenedAsync(child, call)));
    }

    private async Task HeldAsync(DelegationRecord delegation, JobId child, JobHold hold, CancellationToken cancellationToken)
    {
        if (await journal.Calls.Take(child).Match(
            call => journal.ReturnEarlyAsync(child, call, ToolAnswers.Held(call.Item, child, hold), cancellationToken),
            () => Task.FromResult(false)))
        {
            return;
        }

        foreach (var parent in delegation.Parent.Match<JobId[]>(found => [found], () => []))
        {
            _ = await jobs.NoteAsync(parent, ToolAnswers.HeldNote(delegation, child, hold), cancellationToken);
        }
    }

    private async Task StopAsync(JobId child, CallRef call, CancellationToken cancellationToken)
    {
        var known = journal.OfChild(child);

        if (known.Bind(record => record.Report).IsSome || journal.Calls.IsOpen(child))
        {
            var stopped = (await jobs.DiscardAsync(child, cancellationToken)).IsSuccess;
            await ReturnedAsync(call, stopped ? ToolAnswers.Stopped(call.Item, child) : ToolAnswers.NotStopped(call.Item), cancellationToken);

            return;
        }

        journal.Calls.Open(child, call);

        if (!(await jobs.DiscardAsync(child, cancellationToken)).IsSuccess && journal.Calls.Take(child).IsSome)
        {
            await ReturnedAsync(call, ToolAnswers.NotStopped(call.Item), cancellationToken);
        }
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
