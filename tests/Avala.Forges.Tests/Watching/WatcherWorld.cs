using Avala.Agents.Contracts.Sessions;
using Avala.Forges.Connecting;
using Avala.Forges.Contracts;
using Avala.Forges.Delivering;
using Avala.Forges.Policy;
using Avala.Forges.Watching;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Forges.Tests.Watching;

internal sealed class WatcherWorld : IAsyncDisposable
{
    public const string Head = "6dcb09b5b57875f334f61aebed695e2e4193db5e";

    public const string Worktree = "/work/fix";

    public static readonly TimeSpan Poll = TimeSpan.FromSeconds(60);

    public static readonly PullRequestRef Pull = new(7, new Uri("https://forge.example/octo/shop/pulls/7"), "avala/fix", "main");

    private static readonly ForgeName Connection = new("work");

    public WatcherWorld()
    {
        Book = new WatchBook(Store, Bus);
        var connections = new ForgeConnections(new DeclaredForge(), [Forge], new DirectTransports());
        Watcher = new PullRequestWatcher(Book, new PullRequestReader(connections), new Waker(Deliveries, new JobPlaces(Catalog, new FoundWorkspaces()), Git), Clock);
    }

    public JobId Job { get; } = new(Guid.CreateVersion7());

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 3, 2, 9, 0, 0, TimeSpan.Zero));

    public DateTimeOffset Start { get; } = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

    public RecordingBus Bus { get; } = new();

    public MemoryWatchStore Store { get; } = new();

    public ScriptedForge Forge { get; } = new();

    public FakeDeliveries Deliveries { get; } = new();

    public FakeCatalog Catalog { get; } = new();

    public FetchingGit Git { get; } = new();

    public WatchBook Book { get; }

    public PullRequestWatcher Watcher { get; }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static Result<PullRequestState, ForgeError> Observing(CheckStatus build, Mergeability mergeability = Mergeability.Mergeable) =>
        new PullRequestState(Pull, PullRequestLifecycle.Open, Head, mergeability) { Checks = [new CheckRun("build", build, Option<Uri>.None, "1 test failed")] };

    public static PullRequestWatchState Watch(JobId job) => new(job, Connection, Pull, OnPullRequest.WakeOnCi, 3);

    public async Task<WatcherWorld> WatchingAsync(Func<PullRequestWatchState, PullRequestWatchState> shape)
    {
        Store.Kept.Add(new KeptWatch(shape(Watch(Job)), "origin", "https://forge.example/octo/shop.git", []));
        await Book.RunAsync(Cancellation);

        return this;
    }

    public async Task<PullRequestWatchState> RefreshAsync() => Outcomes.Succeeds(await Watcher.RefreshAsync(Job, Cancellation));

    public async Task<PullRequestWatchState> ChangedAsync(Func<PullRequestWatchState, bool> match) =>
        (await Bus.WaitForAsync<PullRequestWatchChanged>(changed => changed.State.Job == Job && match(changed.State), Cancellation)).State;

    public Task SettledAsync() => Watcher.RefreshAsync(new JobId(Guid.Empty), Cancellation);

    public async ValueTask DisposeAsync() => await Watcher.DisposeAsync();

    private sealed class DeclaredForge : IForgeFile
    {
        public ValueTask<Result<ForgeSettings, ForgeError>> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<ForgeSettings, ForgeError>.Success(
                new ForgeSettings([new ForgeDeclaration(Connection, ScriptedForge.Id, new Uri("https://forge.example/api"), CredentialSource.None, Option<string>.None)], Poll)));
    }

    private sealed class DirectTransports : IForgeTransports
    {
        public IForgeApi Open(ForgeDeclaration declaration, IForge forge, ForgeTarget target) => new Unused();

        public Option<ForgeError> Problem(ForgeDeclaration declaration) => Option<ForgeError>.None;
    }

    private sealed class Unused : IForgeApi
    {
        public ValueTask<Result<ForgeResponse, ForgeError>> SendAsync(ForgeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FoundWorkspaces : IWorkspaces
    {
        public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> FindAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<WorkspaceInfo, WorkspaceFailure>.Success(new WorkspaceInfo(workspace, Worktree, Pull.Head, "abc1234")));

        public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> PrepareAsync(WorkspaceRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<CheckpointInfo, WorkspaceFailure>> CheckpointAsync(WorkspaceId workspace, string label, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<WorkspaceId, WorkspaceFailure>> RemoveAsync(WorkspaceId workspace, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> FindAtAsync(string folder, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<WorktreeReconciliation> ReconcileAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<WorktreeReconciliation> CleanAsync(WorktreeReconciliation found, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

internal sealed class MemoryWatchStore : IWatchStore
{
    public List<KeptWatch> Kept { get; } = [];

    public Task<IReadOnlyList<KeptWatch>> WatchesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<KeptWatch>>([.. Kept]);

    public Task KeepAsync(KeptWatch watch, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<WakeUpRecord>> WakeUpsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WakeUpRecord>>([]);

    public Task AddAsync(WakeUpRecord wakeUp, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class ScriptedForge : IForge
{
    public const string Id = "scripted";

    private int reads;

    public ForgeInfo Info { get; } = new(Id, "Scripted");

    public Result<PullRequestState, ForgeError> Observed { get; set; } = WatcherWorld.Observing(CheckStatus.Pending);

    public int Reads => Volatile.Read(ref reads);

    public List<string> Comments { get; } = [];

    public Option<CliCall> CliFor(ForgeRequest request, ForgeTarget target) => Option<CliCall>.None;

    public ValueTask<Result<Option<PullRequestRef>, ForgeError>> FindAsync(ForgeContext context, string head, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<PullRequestRef, ForgeError>> OpenAsync(ForgeContext context, PullRequestDraft draft, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<PullRequestState, ForgeError>> ReadAsync(ForgeContext context, PullRequestRef pullRequest, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref reads);

        return ValueTask.FromResult(Observed);
    }

    public ValueTask<Result<CommentRef, ForgeError>> CommentAsync(ForgeContext context, PullRequestRef pullRequest, string body, CancellationToken cancellationToken)
    {
        Comments.Add(body);

        return ValueTask.FromResult(Result<CommentRef, ForgeError>.Success(new CommentRef("1", Option<Uri>.None)));
    }
}

internal sealed class FakeDeliveries : IOpenDeliveries
{
    public Result<JobContinuation, JobRejection> Reopening { get; set; } = Result<JobContinuation, JobRejection>.Failure(JobRejection.UnknownJob);

    public List<string> Feedback { get; } = [];

    public TaskCompletionSource<string> Redelivered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ValueTask<Result<JobApproval, JobRejection>> ApproveThroughAsync(JobId job, string strategy, CancellationToken cancellationToken)
    {
        Redelivered.TrySetResult(strategy);

        return ValueTask.FromResult(Result<JobApproval, JobRejection>.Success(new JobApproval(job, new ApprovalDelivery(strategy, WatcherWorld.Pull.Head, Option<string>.None))));
    }

    public ValueTask<Result<JobContinuation, JobRejection>> ReopenAsync(JobId job, string feedback, CancellationToken cancellationToken)
    {
        Feedback.Add(feedback);

        return ValueTask.FromResult(Reopening);
    }

    public void Continues(JobId job) =>
        Reopening = new JobContinuation(job, new SessionId(Guid.CreateVersion7()), ContinuedIn.ResumedConversation);
}

internal sealed class FakeCatalog : IJobCatalog
{
    public Option<JobStatus> Status { get; set; } = JobStatus.Approved;

    public ValueTask<Option<JobHistory>> HistoryAsync(JobId job, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Status.Map(status => new JobHistory(
            new JobSummary(job, "/repo", "Fix the test", DateTimeOffset.UnixEpoch, status, Option<Agents.Contracts.Connections.ConnectionName>.None, Option<Autonomy>.None, new WorkspaceId(Guid.CreateVersion7())),
            [],
            [])));

    public ValueTask<IReadOnlyList<JobSummary>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<IReadOnlyList<JobSummary>> ChildrenAsync(JobId parent, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Option<JobTree>> TreeAsync(JobId root, CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class FetchingGit : IGitRemote
{
    public List<(string Worktree, string Remote, string Branch)> Fetched { get; } = [];

    public ValueTask<Result<string, ForgeError>> FetchAsync(string worktree, string remote, string branch, CancellationToken cancellationToken)
    {
        Fetched.Add((worktree, remote, branch));

        return ValueTask.FromResult(Result<string, ForgeError>.Success(string.Empty));
    }

    public ValueTask<Result<string, ForgeError>> UrlAsync(string worktree, string remote, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<string, ForgeError>> PushAsync(string worktree, string remote, string branch, CancellationToken cancellationToken) => throw new NotSupportedException();
}
