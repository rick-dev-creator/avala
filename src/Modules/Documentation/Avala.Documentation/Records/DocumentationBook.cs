using System.Collections.Immutable;
using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Documentation.Records;

internal sealed record PendingWriter(JobId Writer, int Number, VersionKind Kind, string Plan);

internal sealed record DocumentedJob(JobId Job, FeatureChoice Choice)
{
    public ImmutableList<FeatureVersion> Versions { get; init; } = [];

    public Option<PendingWriter> Writer { get; init; }

    public string LastPlan { get; init; } = string.Empty;

    public ImmutableList<string> Plan { get; init; } = [];

    public ImmutableList<ChangeRequest> Requests { get; init; } = [];

    public bool Planned => Versions.Count > 0 || Writer.IsSome || LastPlan.Length > 0 || Plan.Count > 0;

    public IReadOnlyList<ChangeRequest> Linked(FeatureVersion version) =>
        version.Kind == VersionKind.AsBuilt ? Requests : [.. Requests.Select(request => request.Version.IsSome ? request : request with { Version = version.Number })];

    public Option<JobDocumentation> Public => Choice.Feature.Map(
        feature => new JobDocumentation(Job, feature) { Versions = Versions, Writing = Writer.Map(pending => pending.Writer), Requests = Requests, Planned = Planned });
}

internal interface IDocumentationStore
{
    Task<IReadOnlyList<DocumentedJob>> ReadAsync(CancellationToken cancellationToken);

    Task ChooseAsync(JobId job, FeatureChoice choice, CancellationToken cancellationToken);

    Task WritingAsync(JobId job, PendingWriter writer, CancellationToken cancellationToken);

    Task WrittenAsync(JobId job, FeatureVersion version, string plan, CancellationToken cancellationToken);

    Task RequestedAsync(JobId job, ChangeRequest request, CancellationToken cancellationToken);

    Task PlannedAsync(JobId job, IReadOnlyList<string> plan, CancellationToken cancellationToken);

    Task ForgetAsync(JobId job, JobId writer, string plan, CancellationToken cancellationToken);
}

internal sealed class DocumentationBook(IDocumentationStore store, IEventBus bus, TimeProvider time) : IStartupTask
{
    private ImmutableDictionary<JobId, DocumentedJob> jobs = ImmutableDictionary<JobId, DocumentedJob>.Empty;

    public async Task RunAsync(CancellationToken cancellationToken) =>
        Volatile.Write(ref jobs, (await store.ReadAsync(cancellationToken)).ToImmutableDictionary(job => job.Job));

    public Option<DocumentedJob> Of(JobId job) => Volatile.Read(ref jobs).TryGetValue(job, out var found) ? found : Option<DocumentedJob>.None;

    public Option<DocumentedJob> OfWriter(JobId writer) =>
        Volatile.Read(ref jobs).Values.FirstOrDefault(job => job.Writer.Match(pending => pending.Writer == writer, () => false)).ToOption();

    public IReadOnlyList<DocumentedJob> Pending() => [.. Volatile.Read(ref jobs).Values.Where(job => job.Writer.IsSome)];

    public async Task<DocumentedJob> ChooseAsync(JobId job, FeatureChoice choice, CancellationToken cancellationToken)
    {
        await store.ChooseAsync(job, choice, cancellationToken);
        var changed = Change(job, found => found with { Choice = choice }, choice);
        await bus.PublishAsync(new FeatureChosen(job, choice), cancellationToken);

        return changed;
    }

    public async Task WritingAsync(JobId job, PendingWriter writer, CancellationToken cancellationToken)
    {
        await store.WritingAsync(job, writer, cancellationToken);
        Change(job, found => found with { Writer = writer }, FeatureChoice.Undocumented);
        await bus.PublishAsync(new VersionWriting(job, writer.Writer, writer.Number, writer.Kind), cancellationToken);
    }

    public async Task RequestedAsync(JobId job, RequestKind kind, string text, int attempt, CancellationToken cancellationToken)
    {
        var request = new ChangeRequest(Of(job).Match(found => found.Requests.Count, () => 0) + 1, kind, text, time.GetUtcNow(), attempt);
        await store.RequestedAsync(job, request, cancellationToken);
        Change(job, found => found with { Requests = found.Requests.Add(request) }, FeatureChoice.Undocumented);
        await bus.PublishAsync(new ChangeRequested(job, request), cancellationToken);
    }

    public async Task<DocumentedJob> PlannedAsync(DocumentedJob documented, IReadOnlyList<string> plan, CancellationToken cancellationToken)
    {
        if (documented.Plan.SequenceEqual(plan, StringComparer.Ordinal))
        {
            return documented;
        }

        await store.PlannedAsync(documented.Job, plan, cancellationToken);

        return Change(documented.Job, found => found with { Plan = [.. plan] }, documented.Choice);
    }

    public async Task WrittenAsync(JobId job, Feature feature, FeatureVersion version, string plan, CancellationToken cancellationToken)
    {
        var stamped = version with { At = time.GetUtcNow() };
        await store.WrittenAsync(job, stamped, plan, cancellationToken);
        Change(job, found => found with { Versions = found.Versions.Add(stamped), Writer = Option<PendingWriter>.None, LastPlan = plan, Requests = [.. found.Linked(stamped)] }, FeatureChoice.Undocumented);
        await bus.PublishAsync(new VersionWritten(job, feature, stamped), cancellationToken);
    }

    public async Task NotWrittenAsync(JobId job, VersionKind kind, VersionFailure failure, CancellationToken cancellationToken)
    {
        var tried = Of(job).Bind(found => found.Writer);

        await tried.Match(pending => store.ForgetAsync(job, pending.Writer, pending.Plan, cancellationToken), () => Task.CompletedTask);
        Change(job, found => found with { Writer = Option<PendingWriter>.None, LastPlan = tried.Match(pending => pending.Plan, () => found.LastPlan) }, FeatureChoice.Undocumented);
        await bus.PublishAsync(new VersionNotWritten(job, kind, failure), cancellationToken);
    }

    private DocumentedJob Change(JobId job, Func<DocumentedJob, DocumentedJob> change, FeatureChoice choice)
    {
        var changed = change(Of(job).Match(found => found, () => new DocumentedJob(job, choice)));
        ImmutableInterlocked.Update(ref jobs, current => current.SetItem(job, changed));

        return changed;
    }
}
