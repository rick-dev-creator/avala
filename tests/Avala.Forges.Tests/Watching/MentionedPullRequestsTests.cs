using Avala.Forges.Connecting;
using Avala.Forges.Contracts;
using Avala.Forges.Delivering;
using Avala.Forges.Policy;
using Avala.Forges.Tests.Publishing;
using Avala.Forges.Watching;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Forges.Tests.Watching;

public sealed class MentionedPullRequestsTests
{
    private const string Origin = "https://hub.example/ada/shop.git";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ALinkToAPullRequestOfOriginIsNamedWhenNoConnectionHostsOriginAsync()
    {
        var mentioned = Outcomes.Present(await Mentions(connected: false).UnfollowedAsync(JobId.New(), "Opened https://hub.example/ada/shop/pulls/7.", Cancellation));

        Assert.Equal((7, "hub.example"), (mentioned.Number, mentioned.Link.Host));
    }

    [Fact]
    public async Task ALinkToAnotherRepositoryOrToSomethingElseThanAPullRequestIsNotNamedAsync() =>
        Assert.True((await Mentions(connected: false).UnfollowedAsync(JobId.New(), "See https://hub.example/ada/other/pulls/7 and https://hub.example/ada/shop/issues/7", Cancellation)).IsNone);

    [Fact]
    public async Task WithAConnectionHostingOriginNothingIsNamedSinceTheWatchFollowsItAsync() =>
        Assert.True((await Mentions(connected: true).UnfollowedAsync(JobId.New(), "Opened https://hub.example/ada/shop/pulls/7.", Cancellation)).IsNone);

    private static MentionedPullRequests Mentions(bool connected) =>
        new(
            new JobPlaces(new FakeCatalog(), new Worktrees()),
            new OriginOnly(),
            new ForgeConnections(new Declared(connected), [new HostingForge()], new Transports()),
            [new HostingForge()]);

    private sealed class Declared(bool connected) : IForgeFile
    {
        public ValueTask<Result<ForgeSettings, ForgeError>> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<ForgeSettings, ForgeError>.Success(new ForgeSettings(
                connected ? [new ForgeDeclaration(new ForgeName("hub"), HostingForge.Id, new Uri("https://api.hub.example"), CredentialSource.None, Option<string>.None)] : [],
                ForgeSettings.DefaultPoll)));
    }

    private sealed class Transports : IForgeTransports
    {
        public IForgeApi Open(ForgeDeclaration declaration, IForge forge, ForgeTarget target) => throw new NotSupportedException();

        public Option<ForgeError> Problem(ForgeDeclaration declaration) => Option<ForgeError>.None;
    }

    private sealed class OriginOnly : IGitRemote
    {
        public ValueTask<Result<string, ForgeError>> UrlAsync(string worktree, string remote, CancellationToken cancellationToken) => ValueTask.FromResult(Result<string, ForgeError>.Success(Origin));

        public ValueTask<Result<string, ForgeError>> PushAsync(string worktree, string remote, string branch, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<string, ForgeError>> FetchAsync(string worktree, string remote, string branch, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<string>> CheckedOutAsync(string worktree, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<bool> PushedAsync(string worktree, string remote, string branch, string commit, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Worktrees : IWorkspaces
    {
        public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> FindAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<WorkspaceInfo, WorkspaceFailure>.Success(new WorkspaceInfo(workspace, "/worktrees/shop", "avala/shop", "abc1234")));

        public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> PrepareAsync(WorkspaceRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<CheckpointInfo, WorkspaceFailure>> CheckpointAsync(WorkspaceId workspace, string label, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<WorkspaceId, WorkspaceFailure>> RemoveAsync(WorkspaceId workspace, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> FindAtAsync(string folder, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<WorktreeReconciliation> ReconcileAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<WorktreeReconciliation> CleanAsync(WorktreeReconciliation found, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
