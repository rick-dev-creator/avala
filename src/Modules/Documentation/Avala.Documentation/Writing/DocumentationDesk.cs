using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Documentation.Contracts;
using Avala.Documentation.Records;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Documentation.Writing;

internal sealed class DocumentationDesk(DocumentedJobs subjects, WriterLauncher launcher, VersionKeeper keeper, DocumentationBook book)
    : IHandle<JobSessionStarted>, IHandle<AgentActivity>, IHandle<JobProgressed>, IHandle<StartupCompleted>
{
    private static readonly JobStatus[] Settled = [JobStatus.AwaitingReview, JobStatus.NeedsHelp, JobStatus.Failed, JobStatus.Discarded, JobStatus.Approved];

    private readonly Dictionary<SessionId, JobId> sessions = [];

    public ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        sessions[integrationEvent.Session] = integrationEvent.Job;

        return ValueTask.CompletedTask;
    }

    public async ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken)
    {
        switch (integrationEvent.Event)
        {
            case PlanUpdated { Steps.Count: > 0 } updated when sessions.TryGetValue(updated.Session, out var job):
                await (await subjects.OfAsync(job, cancellationToken)).Match(
                    subject => PlannedAsync(subject, updated, cancellationToken),
                    () => Task.CompletedTask);
                break;
            case TurnCompleted completed when sessions.TryGetValue(completed.Session, out var job):
                await WriteAsync(job, cancellationToken);
                break;
            case MessageQueued { Author: MessageAuthor.Person } queued when sessions.TryGetValue(queued.Session, out var job) && queued.Text.Trim().Length > 0:
                await (await subjects.OfAsync(job, cancellationToken)).Match(
                    subject => book.RequestedAsync(job, RequestKind.Steer, queued.Text.Trim(), subject.Place.Attempt, cancellationToken),
                    () => Task.CompletedTask);
                break;
        }
    }

    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken) =>
        await book.OfWriter(integrationEvent.Job).Match(
            documented => SettleAsync(documented, integrationEvent.Status, cancellationToken),
            () => integrationEvent.Status switch
            {
                JobStatus.AwaitingReview => AsBuiltAsync(integrationEvent.Job, cancellationToken),
                JobStatus.Running => AskedAsync(integrationEvent.Job, cancellationToken),
                _ => Task.CompletedTask,
            });

    public async ValueTask HandleAsync(StartupCompleted integrationEvent, CancellationToken cancellationToken)
    {
        foreach (var documented in book.Pending())
        {
            await documented.Writer.Match(
                async pending => await (await subjects.StatusAsync(pending.Writer, cancellationToken)).Match(
                    status => SettleAsync(documented, status, cancellationToken),
                    () => StoppedAsync(documented.Job, pending, cancellationToken)),
                () => Task.CompletedTask);
        }
    }

    private async Task AskedAsync(JobId job, CancellationToken cancellationToken) =>
        await (await subjects.OfAsync(job, cancellationToken)).Match(subject => AttemptsAskedAsync(subject, cancellationToken), () => Task.CompletedTask);

    private async Task AttemptsAskedAsync(Subject subject, CancellationToken cancellationToken)
    {
        var job = subject.Place.Job;

        foreach (var attempt in await subjects.AttemptsAsync(job, cancellationToken))
        {
            var asked = attempt.Author == MessageAuthor.Person ? Requested(attempt.Origin) : Option<RequestKind>.None;
            var text = attempt.Guidance.Match(guidance => guidance.Trim(), () => string.Empty);

            if (asked.IsSome && text.Length > 0 && !subject.Documented.Requests.Any(request => request.Kind != RequestKind.Steer && request.Attempt == attempt.Number))
            {
                await book.RequestedAsync(job, asked.Match(kind => kind, () => RequestKind.Message), text, attempt.Number, cancellationToken);
            }
        }
    }

    private static Option<RequestKind> Requested(AttemptOrigin origin) => origin switch
    {
        AttemptOrigin.SendBack => RequestKind.SendBack,
        AttemptOrigin.Hint or AttemptOrigin.Device or AttemptOrigin.Reopen => RequestKind.Message,
        _ => Option<RequestKind>.None,
    };

    private async Task PlannedAsync(Subject subject, PlanUpdated updated, CancellationToken cancellationToken)
    {
        var documented = await book.PlannedAsync(subject.Documented, [.. updated.Steps.Select(step => step.Title)], cancellationToken);

        if (updated.Steps.Any(step => step.Status != PlanStepStatus.Pending))
        {
            await LaunchAsync(subject with { Documented = documented }, cancellationToken);
        }
    }

    private async Task WriteAsync(JobId job, CancellationToken cancellationToken) =>
        await (await subjects.OfAsync(job, cancellationToken)).Match(subject => LaunchAsync(subject, cancellationToken), () => Task.CompletedTask);

    private async Task LaunchAsync(Subject subject, CancellationToken cancellationToken)
    {
        var plan = subject.Documented.Plan;
        var signature = string.Join('\n', plan);

        if (plan.Count == 0 || subject.Documented.Writer.IsSome || signature == subject.Documented.LastPlan)
        {
            return;
        }

        var job = subject.Place.Job;
        var kind = subject.Documented.Versions.Count == 0 ? VersionKind.Proposed : VersionKind.Deviation;

        await (await launcher.StartAsync(subject, plan, kind, cancellationToken)).Match(
            launched => book.WritingAsync(job, new PendingWriter(launched.Writer, launched.Number, kind, signature), cancellationToken),
            _ => NotWrittenAsync(job, kind, VersionFailure.WriterRefused, Option<HoldReason>.None, cancellationToken));
    }

    private async Task SettleAsync(DocumentedJob documented, JobStatus status, CancellationToken cancellationToken)
    {
        if (!Settled.Contains(status))
        {
            return;
        }

        await documented.Writer.Match(
            async pending =>
            {
                await (status == JobStatus.AwaitingReview ? KeepAsync(documented, pending, cancellationToken) : StoppedAsync(documented.Job, pending, cancellationToken));

                if (status is JobStatus.AwaitingReview or JobStatus.NeedsHelp)
                {
                    await launcher.DiscardAsync(pending.Writer, cancellationToken);
                }

                await NextAsync(documented.Job, cancellationToken);
            },
            () => Task.CompletedTask);
    }

    private async Task KeepAsync(DocumentedJob documented, PendingWriter pending, CancellationToken cancellationToken)
    {
        var writer = await subjects.PlaceAsync(pending.Writer, cancellationToken);

        await (await subjects.OfAsync(documented.Job, cancellationToken)).Match(
            async subject => await (await writer.Match(
                    place => keeper.KeepAsync(place, subject, pending, cancellationToken),
                    () => Task.FromResult(Result<FeatureVersion, VersionFailure>.Failure(VersionFailure.NoDraft))))
                .Match(
                    version => WrittenAsync(subject, version, pending.Plan, cancellationToken),
                    failure => NotWrittenAsync(documented.Job, pending.Kind, failure, Option<HoldReason>.None, cancellationToken)),
            () => NotWrittenAsync(documented.Job, pending.Kind, VersionFailure.Unwritable, Option<HoldReason>.None, cancellationToken));
    }

    private async Task StoppedAsync(JobId job, PendingWriter pending, CancellationToken cancellationToken) =>
        await NotWrittenAsync(job, pending.Kind, VersionFailure.WriterStopped, await subjects.HoldAsync(pending.Writer, cancellationToken), cancellationToken);

    private async Task NotWrittenAsync(JobId job, VersionKind kind, VersionFailure failure, Option<HoldReason> hold, CancellationToken cancellationToken) =>
        await book.NotWrittenAsync(job, new UnwrittenVersion(kind, failure, default, Math.Max(1, (await subjects.AttemptsAsync(job, cancellationToken)).Count)) { Hold = hold }, cancellationToken);

    private async Task WrittenAsync(Subject subject, FeatureVersion version, string plan, CancellationToken cancellationToken)
    {
        await subjects.CommitAsync(subject.Place.Job, $"Document {subject.Folder.Feature.Title}, version {version.Number}", cancellationToken);
        await book.WrittenAsync(subject.Place.Job, subject.Folder.Feature, version, plan, cancellationToken);
    }

    private async Task NextAsync(JobId job, CancellationToken cancellationToken)
    {
        await WriteAsync(job, cancellationToken);

        if ((await subjects.PlaceAsync(job, cancellationToken)).Match(place => place.Status == JobStatus.AwaitingReview, () => false))
        {
            await AsBuiltAsync(job, cancellationToken);
        }
    }

    private async Task AsBuiltAsync(JobId job, CancellationToken cancellationToken) =>
        await (await subjects.OfAsync(job, cancellationToken)).Match(
            async subject =>
            {
                if (subject.Documented.Writer.IsSome || subject.Documented.Versions.Count == 0)
                {
                    return;
                }

                await (await keeper.AsBuiltAsync(subject, cancellationToken)).Match(
                    version => WrittenAsync(subject, version, subject.Documented.LastPlan, cancellationToken),
                    failure => NotWrittenAsync(job, VersionKind.AsBuilt, failure, Option<HoldReason>.None, cancellationToken));
            },
            () => Task.CompletedTask);
}
