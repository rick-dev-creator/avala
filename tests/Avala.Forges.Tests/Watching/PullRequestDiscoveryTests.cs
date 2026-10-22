using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
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

public sealed class PullRequestDiscoveryTests : IAsyncDisposable
{
    private const string Origin = "https://forge.example/octo/shop.git";

    private static readonly ForgeName Work = new("work");

    private static readonly PullRequestRef Pull = new(7, new Uri("https://forge.example/octo/shop/pulls/7"), "avala/fix", "main");

    private readonly JobId job = JobId.New();
    private readonly RecordingBus bus = new();
    private readonly BranchForge forge = new();
    private readonly ReportedBook reported = new(new Reports());
    private readonly WatchAdoption adoption;
    private readonly PullRequestDiscovery discovery;

    public PullRequestDiscoveryTests()
    {
        var connections = new ForgeConnections(new Declared(), [forge], new Transports());
        var git = new HeldGit();
        adoption = new WatchAdoption(new WatchBook(new MemoryWatchStore(), bus), new PullRequestReader(connections), new FakeTimeProvider());
        discovery = new PullRequestDiscovery(
            new JobPlaces(new FakeCatalog(), new FoundWorkspaces()),
            new PullRequestFinder(new EffectiveRules(new DeclaredRules(), git, connections), git, connections),
            adoption,
            reported);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobThatGoesToReviewWithAPullRequestOpenOnItsBranchHasItWatchedAsync()
    {
        await discovery.HandleAsync(new JobProgressed(job, JobStatus.AwaitingReview), Cancellation);

        Assert.Equal(new PullRequestOpened(job, Work, Pull), bus.Published.OfType<PullRequestOpened>().Single());
        Assert.True(adoption.Follows(job));
    }

    [Fact]
    public async Task AJobWhosePullRequestIsAlreadyWatchedIsNotLookedUpAgainAsync()
    {
        await discovery.HandleAsync(new JobProgressed(job, JobStatus.AwaitingReview), Cancellation);
        await discovery.HandleAsync(new JobProgressed(job, JobStatus.NeedsHelp), Cancellation);

        Assert.Equal(1, forge.Finds);
        Assert.Single(bus.Published.OfType<PullRequestOpened>());
    }

    [Fact]
    public async Task AJobStillAtWorkIsNotLookedUpAsync()
    {
        await discovery.HandleAsync(new JobProgressed(job, JobStatus.Running), Cancellation);

        Assert.Equal(0, forge.Finds);
    }

    [Fact]
    public async Task AJobThatEndsForgetsTheLinksItsAgentReportedAsync()
    {
        var session = new SessionId(Guid.CreateVersion7());
        var turn = new TurnId(Guid.CreateVersion7());
        await discovery.HandleAsync(new JobSessionStarted(job, session), Cancellation);
        await discovery.HandleAsync(new AgentActivity(new ItemStarted(session, turn, new ItemId("reply"), ItemKind.Message, "Reply")), Cancellation);
        await discovery.HandleAsync(new AgentActivity(new ItemProgressed(session, turn, new ItemId("reply"), "Opened https://forge.example/octo/shop/pulls/7 ")), Cancellation);
        await discovery.HandleAsync(new AgentActivity(new ItemCompleted(session, turn, new ItemId("reply"), ItemOutcome.Succeeded)), Cancellation);
        var said = (await reported.OfAsync(job, Cancellation)).Links;

        await discovery.HandleAsync(new JobProgressed(job, JobStatus.Approved), Cancellation);

        Assert.Equal([Pull.Link], said);
        Assert.Empty((await reported.OfAsync(job, Cancellation)).Links);
    }

    public ValueTask DisposeAsync() => reported.DisposeAsync();

    private sealed class BranchForge : IForge
    {
        public const string Id = "branch";

        private int finds;

        public int Finds => Volatile.Read(ref finds);

        public ForgeInfo Info { get; } = new(Id, "Branch");

        public Option<CliCall> CliFor(ForgeRequest request, ForgeTarget target) => Option<CliCall>.None;

        public ValueTask<Result<Option<PullRequestRef>, ForgeError>> FindAsync(ForgeContext context, string head, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref finds);

            return ValueTask.FromResult(Result<Option<PullRequestRef>, ForgeError>.Success(head == Pull.Head ? Pull : Option<PullRequestRef>.None));
        }

        public ValueTask<Result<PullRequestRef, ForgeError>> OpenAsync(ForgeContext context, PullRequestDraft draft, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<PullRequestState, ForgeError>> ReadAsync(ForgeContext context, PullRequestRef pullRequest, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<PullRequestState, ForgeError>.Success(new PullRequestState(pullRequest, PullRequestLifecycle.Open, "abc1234", Mergeability.Mergeable)));

        public ValueTask<Result<CommentRef, ForgeError>> CommentAsync(ForgeContext context, PullRequestRef pullRequest, string body, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class HeldGit : IGitRemote
    {
        public ValueTask<Result<string, ForgeError>> UrlAsync(string worktree, string remote, CancellationToken cancellationToken) => ValueTask.FromResult(Result<string, ForgeError>.Success(Origin));

        public ValueTask<Result<string, ForgeError>> PushAsync(string worktree, string remote, string branch, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<string, ForgeError>> FetchAsync(string worktree, string remote, string branch, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<string>> CheckedOutAsync(string worktree, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<string>>([]);

        public ValueTask<bool> PushedAsync(string worktree, string remote, string branch, string commit, CancellationToken cancellationToken) => ValueTask.FromResult(true);

        public ValueTask<bool> HeldAsync(string worktree, string commit, CancellationToken cancellationToken) => ValueTask.FromResult(true);
    }

    private sealed class FoundWorkspaces : IWorkspaces
    {
        public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> FindAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<WorkspaceInfo, WorkspaceFailure>.Success(new WorkspaceInfo(workspace, "/work/fix", Pull.Head, "abc1234")));

        public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> PrepareAsync(WorkspaceRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<CheckpointInfo, WorkspaceFailure>> CheckpointAsync(WorkspaceId workspace, string label, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<WorkspaceId, WorkspaceFailure>> RemoveAsync(WorkspaceId workspace, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> FindAtAsync(string folder, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<WorktreeReconciliation> ReconcileAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<WorktreeReconciliation> CleanAsync(WorktreeReconciliation found, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Reports : IReportedStore
    {
        public Task<IReadOnlyList<KeptReport>> ReportsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<KeptReport>>([]);

        public Task KeepReportAsync(KeptReport report, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ForgetReportAsync(JobId job, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Declared : IForgeFile
    {
        public ValueTask<Result<ForgeSettings, ForgeError>> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<ForgeSettings, ForgeError>.Success(new ForgeSettings(
                [new ForgeDeclaration(Work, BranchForge.Id, new Uri("https://forge.example/api"), CredentialSource.None, Option<string>.None)],
                ForgeSettings.DefaultPoll)));
    }

    private sealed class DeclaredRules : IPullRequestRules
    {
        public ValueTask<Result<Option<PullRequestRules>, ForgeError>> OfWorktreeAsync(string worktree, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<Option<PullRequestRules>, ForgeError>.Success(new PullRequestRules(Work, PullRequestRules.DefaultRemote, OnPullRequest.WatchOnly, PullRequestRules.DefaultWakeUps)));
    }

    private sealed class Transports : IForgeTransports
    {
        public IForgeApi Open(ForgeDeclaration declaration, IForge forge, ForgeTarget target) => new Unused();

        public Option<ForgeError> Problem(ForgeDeclaration declaration) => Option<ForgeError>.None;
    }

    private sealed class Unused : IForgeApi
    {
        public ValueTask<Result<ForgeResponse, ForgeError>> SendAsync(ForgeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
