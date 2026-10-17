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
    private readonly Dictionary<JobId, IReadOnlyList<string>> plans = [];

    public ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        sessions[integrationEvent.Session] = integrationEvent.Job;

        return ValueTask.CompletedTask;
    }

    public async ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Event is PlanUpdated { Steps.Count: > 0 } updated && sessions.TryGetValue(updated.Session, out var job))
        {
            plans[job] = [.. updated.Steps.Select(step => step.Title)];
            await WriteAsync(job, cancellationToken);
        }
    }

    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken) =>
        await book.OfWriter(integrationEvent.Job).Match(
            documented => SettleAsync(documented, integrationEvent.Status, cancellationToken),
            () => integrationEvent.Status == JobStatus.AwaitingReview ? AsBuiltAsync(integrationEvent.Job, cancellationToken) : Task.CompletedTask);

    public async ValueTask HandleAsync(StartupCompleted integrationEvent, CancellationToken cancellationToken)
    {
        foreach (var documented in book.Pending())
        {
            await documented.Writer.Match(
                async pending => await (await subjects.StatusAsync(pending.Writer, cancellationToken)).Match(
                    status => SettleAsync(documented, status, cancellationToken),
                    () => book.NotWrittenAsync(documented.Job, pending.Kind, VersionFailure.WriterStopped, cancellationToken)),
                () => Task.CompletedTask);
        }
    }

    private async Task WriteAsync(JobId job, CancellationToken cancellationToken)
    {
        if (plans.TryGetValue(job, out var plan))
        {
            await (await subjects.OfAsync(job, cancellationToken)).Match(subject => LaunchAsync(subject, plan, cancellationToken), () => Task.CompletedTask);
        }
    }

    private async Task LaunchAsync(Subject subject, IReadOnlyList<string> plan, CancellationToken cancellationToken)
    {
        var signature = string.Join('\n', plan);

        if (subject.Documented.Writer.IsSome || signature == subject.Documented.LastPlan)
        {
            return;
        }

        var job = subject.Place.Job;
        var kind = subject.Documented.Versions.Count == 0 ? VersionKind.Proposed : VersionKind.Deviation;

        await (await launcher.StartAsync(subject, plan, kind, cancellationToken)).Match(
            launched => book.WritingAsync(job, new PendingWriter(launched.Writer, launched.Number, kind, signature), cancellationToken),
            _ => book.NotWrittenAsync(job, kind, VersionFailure.WriterRefused, cancellationToken));
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
                await (status == JobStatus.AwaitingReview ? KeepAsync(documented, pending, cancellationToken) : book.NotWrittenAsync(documented.Job, pending.Kind, VersionFailure.WriterStopped, cancellationToken));

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
                    version => book.WrittenAsync(documented.Job, subject.Folder.Feature, version, pending.Plan, cancellationToken),
                    failure => book.NotWrittenAsync(documented.Job, pending.Kind, failure, cancellationToken)),
            () => book.NotWrittenAsync(documented.Job, pending.Kind, VersionFailure.Unwritable, cancellationToken));
    }

    private async Task NextAsync(JobId job, CancellationToken cancellationToken) =>
        await ((await subjects.PlaceAsync(job, cancellationToken)).Match(place => place.Status == JobStatus.AwaitingReview, () => false)
            ? AsBuiltAsync(job, cancellationToken)
            : WriteAsync(job, cancellationToken));

    private async Task AsBuiltAsync(JobId job, CancellationToken cancellationToken) =>
        await (await subjects.OfAsync(job, cancellationToken)).Match(
            async subject =>
            {
                if (subject.Documented.Writer.IsSome || subject.Documented.Versions.Count == 0)
                {
                    return;
                }

                await (await keeper.AsBuiltAsync(subject, cancellationToken)).Match(
                    version => book.WrittenAsync(job, subject.Folder.Feature, version, subject.Documented.LastPlan, cancellationToken),
                    failure => book.NotWrittenAsync(job, VersionKind.AsBuilt, failure, cancellationToken));
            },
            () => Task.CompletedTask);
}
