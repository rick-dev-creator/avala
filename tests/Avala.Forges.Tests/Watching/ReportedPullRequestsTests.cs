using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Forges.Connecting;
using Avala.Forges.Contracts;
using Avala.Forges.Delivering;
using Avala.Forges.Policy;
using Avala.Forges.Watching;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Forges.Tests.Watching;

public sealed class ReportedPullRequestsTests
{
    private const string Origin = "https://forge.example/octo/shop.git";

    private static readonly RepositoryAddress Shop = new("forge.example", "octo", "shop");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Opened `https://forge.example/octo/shop/pulls/7`.")]
    [InlineData("**Opened https://forge.example/octo/shop/pulls/7**")]
    [InlineData("Opened _https://forge.example/octo/shop/pulls/7_")]
    [InlineData("| Pull request | https://forge.example/octo/shop/pulls/7|")]
    [InlineData("已打开 https://forge.example/octo/shop/pulls/7。")]
    [InlineData("Opened https://forge.example/octo/shop/pulls/7/files")]
    public void ALinkToAPullRequestOfOriginNamesItsNumberHoweverTheAgentDecoratesIt(string text) =>
        Assert.Equal(
            [7],
            PullRequestLinks.In(text).SelectMany(link => PullRequestLinks.NumberOf(link, Shop, new HashSet<string>(["pulls"], StringComparer.Ordinal)).Match<int[]>(number => [number], () => [])));

    [Fact]
    public void ALinkAtTheStartOfAnItemLongerThanWhatIsKeptIsStillReported()
    {
        var reported = new ReportedLinks();
        var job = JobId.New();
        var session = new SessionId(Guid.CreateVersion7());
        var turn = new TurnId(Guid.CreateVersion7());
        var item = new ItemId("pr");
        reported.Started(job, session);

        reported.Saw(new ItemStarted(session, turn, item, ItemKind.Command, "gh pr create --fill && dotnet test"));
        reported.Saw(new ItemProgressed(session, turn, item, "https://forge.example/octo/shop/pulls/7\n"));
        reported.Saw(new ItemProgressed(session, turn, item, new string('.', ReportedLinks.KeptPerItem)));
        reported.Saw(new ItemCompleted(session, turn, item, ItemOutcome.Succeeded));

        Assert.Equal([new Uri("https://forge.example/octo/shop/pulls/7")], reported.Of(job));
    }

    [Fact]
    public async Task APullRequestTheAgentLinkedManyTimesIsReadFromTheForgeOnceAsync()
    {
        var forge = new CountingForge();
        var connections = new ForgeConnections(new Declared(), [forge], new Transports());
        var git = new OriginGit();
        var finder = new PullRequestFinder(new EffectiveRules(new DeclaredRules(), git, connections), git, connections);
        var link = new Uri("https://forge.example/octo/shop/pulls/7");

        _ = await finder.FindAsync(JobId.New(), new WorkspaceInfo(new WorkspaceId(Guid.CreateVersion7()), "/worktrees/shop", "avala/shop", "abc1234"), [.. Enumerable.Repeat(link, 200)], Cancellation);

        Assert.Equal(1, forge.Reads);
    }

    private sealed class CountingForge : IForge
    {
        public const string Id = "counting";

        public int Reads { get; private set; }

        public ForgeInfo Info { get; } = new(Id, "Counting");

        public Option<CliCall> CliFor(ForgeRequest request, ForgeTarget target) => Option<CliCall>.None;

        public ValueTask<Result<Option<PullRequestRef>, ForgeError>> FindAsync(ForgeContext context, string head, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<Option<PullRequestRef>, ForgeError>.Success(Option<PullRequestRef>.None));

        public ValueTask<Result<PullRequestRef, ForgeError>> OpenAsync(ForgeContext context, PullRequestDraft draft, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<PullRequestState, ForgeError>> ReadAsync(ForgeContext context, PullRequestRef pullRequest, CancellationToken cancellationToken)
        {
            Reads++;

            return ValueTask.FromResult(Result<PullRequestState, ForgeError>.Success(
                new PullRequestState(pullRequest with { Head = "fix/old", Base = "main" }, PullRequestLifecycle.Merged, "abc1234", Mergeability.Unknown)));
        }

        public ValueTask<Result<CommentRef, ForgeError>> CommentAsync(ForgeContext context, PullRequestRef pullRequest, string body, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Declared : IForgeFile
    {
        public ValueTask<Result<ForgeSettings, ForgeError>> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<ForgeSettings, ForgeError>.Success(new ForgeSettings(
                [new ForgeDeclaration(new ForgeName("work"), CountingForge.Id, new Uri("https://forge.example/api"), CredentialSource.None, Option<string>.None)],
                ForgeSettings.DefaultPoll)));
    }

    private sealed class DeclaredRules : IPullRequestRules
    {
        public ValueTask<Result<Option<PullRequestRules>, ForgeError>> OfWorktreeAsync(string worktree, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<Option<PullRequestRules>, ForgeError>.Success(new PullRequestRules(new ForgeName("work"), PullRequestRules.DefaultRemote, OnPullRequest.WatchOnly, PullRequestRules.DefaultWakeUps)));
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

    private sealed class OriginGit : IGitRemote
    {
        public ValueTask<Result<string, ForgeError>> UrlAsync(string worktree, string remote, CancellationToken cancellationToken) => ValueTask.FromResult(Result<string, ForgeError>.Success(Origin));

        public ValueTask<Result<string, ForgeError>> PushAsync(string worktree, string remote, string branch, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<string, ForgeError>> FetchAsync(string worktree, string remote, string branch, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<string>> CheckedOutAsync(string worktree, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<string>>([]);

        public ValueTask<bool> HasBranchAsync(string worktree, string branch, CancellationToken cancellationToken) => ValueTask.FromResult(true);
    }
}
