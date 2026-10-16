using Avala.Agents.Contracts.Connections;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Conversation;
using Avala.Workbench.Drafting;
using Avala.Workbench.Mentioning;
using Avala.Workbench.NewJob;
using Avala.Workbench.Steering;

namespace Avala.Workbench.Tests;

internal static class Composing
{
    public static Composers Of(JobSteering steering, SendKeys keys) => new(steering, keys, Harnesses(new FakeJobs(), new FakeHandoffRequests(), new FakeConnections()), Parts());

    public static AttachmentTrays Trays() => new(new FakeJobAttachments(), new FakeFilePicker(), new FakePictures());

    public static ComposerParts Parts() => new(Trays(), Mentions(), Drafts());

    public static DraftBook Drafts() => new(new FakeDraftStore(), new JobBoard(), TimeProvider.System);

    public static MentionSource Mentions() => new(Avala.Workbench.Tests.Tools.Files(), new FakeRepositoryIndex(), new JobBoard(), TimeProvider.System);

    public static ComposerTools Tools(SendKeys keys, JobId job) => new(keys, Trays().Open(), new MentionsViewModel(job, Mentions()), Drafts());

    public static MentionsViewModel Mentioning(Worktree worktree, params string[] files)
    {
        var index = new FakeRepositoryIndex();
        index.Files.AddRange(files);
        var worked = worktree.Worked();

        return new MentionsViewModel(worktree.Job, new MentionSource(worked, index, new JobBoard(), TimeProvider.System));
    }

    public static JobHarnesses Harnesses(IJobs jobs, IHandoffRequests handoffs, IConnections connections) =>
        new(new JobSwitching(jobs, handoffs, connections), new NewJobReadings(Pages.Readings(new FakeUsage())));

    public static JobHarness Harness(JobId job) => Harnesses(new FakeJobs(), new FakeHandoffRequests(), new FakeConnections()).Open(job);
}

internal sealed class FakeDraftStore : IDraftStore
{
    public Dictionary<JobId, Draft> Kept { get; } = [];

    public int Writes { get; private set; }

    public Task<IReadOnlyDictionary<JobId, Draft>> ReadAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<JobId, Draft>>(new Dictionary<JobId, Draft>(Kept));

    public Task WriteAsync(IReadOnlyDictionary<JobId, Draft> drafts, CancellationToken cancellationToken)
    {
        Kept.Clear();

        foreach (var (job, draft) in drafts)
        {
            Kept[job] = draft;
        }

        Writes++;

        return Task.CompletedTask;
    }
}

internal sealed class FakeHandoffRequests : IHandoffRequests
{
    public List<(JobId Job, ConnectionName To)> Requests { get; } = [];

    public Option<JobRejection> Refusal { get; set; }

    public ValueTask<Result<JobContinuation, JobRejection>> HandOffAsync(JobId job, ConnectionName to, CancellationToken cancellationToken)
    {
        Requests.Add((job, to));

        return ValueTask.FromResult(Refusal.Match(
            rejection => Result<JobContinuation, JobRejection>.Failure(rejection),
            () => Result<JobContinuation, JobRejection>.Success(new JobContinuation(job, Avala.Agents.Contracts.Sessions.SessionId.New(), ContinuedIn.NewConversation))));
    }
}

internal sealed class FakeRepositoryIndex : Avala.Workspaces.Contracts.IRepositoryIndex
{
    public List<string> Files { get; } = [];

    public List<Avala.Workspaces.Contracts.CommitSummary> Commits { get; } = [];

    public int Listings { get; private set; }

    public ValueTask<IReadOnlyList<string>> FilesAsync(string folder, int limit, CancellationToken cancellationToken)
    {
        Listings++;

        return ValueTask.FromResult<IReadOnlyList<string>>([.. Files.Take(limit)]);
    }

    public ValueTask<IReadOnlyList<Avala.Workspaces.Contracts.CommitSummary>> CommitsAsync(string folder, int count, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<Avala.Workspaces.Contracts.CommitSummary>>([.. Commits.Take(count)]);
}
