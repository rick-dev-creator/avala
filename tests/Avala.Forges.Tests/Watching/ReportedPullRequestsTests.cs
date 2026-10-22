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

        Assert.Equal([new Uri("https://forge.example/octo/shop/pulls/7")], reported.Of(job).Links);
    }

    [Fact]
    public void APullRequestLinkIsKeptWithoutItsQuery() =>
        Assert.Equal([new Uri("https://forge.example/octo/shop/pulls/7")], PullRequestLinks.In("Opened https://forge.example/octo/shop/pulls/7?token=secret "));

    [Fact]
    public void ALinkOnAnotherHostWithOriginsOwnerAndNameIsNotOneOfOriginsPullRequests() =>
        Assert.True(PullRequestLinks.NumberOf(new Uri("https://elsewhere.example/octo/shop/pulls/7"), Shop, new HashSet<string>(["pulls"], StringComparer.Ordinal)).IsNone);

    [Fact]
    public void ALinkSplitAcrossChunksIsReportedWholeAndOnlyLinksShapedLikePullRequestsAreKept()
    {
        var (reported, job, say) = Reporting();

        say(["See https://example.com/docs/guide and https://forge.example/octo/shop/pu", "lls/12 when CI ends."]);

        Assert.Equal([new Uri("https://forge.example/octo/shop/pulls/12")], reported.Of(job).Links);
    }

    [Fact]
    public void ALinkACommandThatCreatesAPullRequestPrintedIsTriedBeforeTheLinksMentionedAfterIt()
    {
        var (reported, job, say) = Reporting();
        var session = new SessionId(Guid.CreateVersion7());
        var turn = new TurnId(Guid.CreateVersion7());
        reported.Started(job, session);
        reported.Saw(new ItemStarted(session, turn, new ItemId("create"), ItemKind.Command, "Run gh pr create --fill") { Input = "gh pr create --fill" });
        reported.Saw(new ItemProgressed(session, turn, new ItemId("create"), "https://forge.example/octo/shop/pulls/7\n"));
        reported.Saw(new ItemCompleted(session, turn, new ItemId("create"), ItemOutcome.Succeeded));

        say([string.Join(' ', Enumerable.Range(20, 6).Select(number => $"https://forge.example/octo/shop/pulls/{number}"))]);

        Assert.Equal(new Uri("https://forge.example/octo/shop/pulls/7"), reported.Of(job).Links[^1]);
    }

    [Fact]
    public void ALinkAnMcpToolThatCreatesAPullRequestReturnedIsTriedBeforeTheLinksMentionedAfterIt()
    {
        var (reported, job, say) = Reporting();
        var session = new SessionId(Guid.CreateVersion7());
        var turn = new TurnId(Guid.CreateVersion7());
        reported.Started(job, session);
        reported.Saw(new ItemStarted(session, turn, new ItemId("create"), ItemKind.Mcp, "mcp__github__create_pull_request"));
        reported.Saw(new ItemProgressed(session, turn, new ItemId("create"), """{"html_url": "https://forge.example/octo/shop/pulls/7"}"""));
        reported.Saw(new ItemCompleted(session, turn, new ItemId("create"), ItemOutcome.Succeeded));

        say([string.Join(' ', Enumerable.Range(20, 6).Select(number => $"https://forge.example/octo/shop/pulls/{number}"))]);

        Assert.Equal(new Uri("https://forge.example/octo/shop/pulls/7"), reported.Of(job).Links[^1]);
    }

    [Fact]
    public void AJobThatEndsForgetsItsUnfinishedItemsAndItsLinks()
    {
        var (reported, job, say) = Reporting();
        say(["Opened https://forge.example/octo/shop/pulls/7 "]);
        var session = new SessionId(Guid.CreateVersion7());
        reported.Started(job, session);
        reported.Saw(new ItemStarted(session, new TurnId(Guid.CreateVersion7()), new ItemId("long"), ItemKind.Command, "dotnet test"));

        reported.Forget(job);

        Assert.Equal((0, 0), (reported.Open, reported.Of(job).Links.Count));
    }

    private static (ReportedLinks Reported, JobId Job, Action<string[]> Say) Reporting()
    {
        var reported = new ReportedLinks();
        var job = JobId.New();
        var session = new SessionId(Guid.CreateVersion7());
        var turn = new TurnId(Guid.CreateVersion7());
        reported.Started(job, session);

        return (reported, job, chunks =>
        {
            var item = new ItemId(Guid.NewGuid().ToString("N"));
            reported.Saw(new ItemStarted(session, turn, item, ItemKind.Message, "Reply"));

            foreach (var chunk in chunks)
            {
                reported.Saw(new ItemProgressed(session, turn, item, chunk));
            }

            reported.Saw(new ItemCompleted(session, turn, item, ItemOutcome.Succeeded));
        });
    }

    [Fact]
    public async Task APullRequestTheAgentLinkedManyTimesIsReadFromTheForgeOnceAsync()
    {
        var forge = new CountingForge();
        var connections = new ForgeConnections(new Declared(), [forge], new Transports());
        var git = new OriginGit();
        var finder = new PullRequestFinder(new EffectiveRules(new DeclaredRules(), git, connections), git, connections);
        var link = new Uri("https://forge.example/octo/shop/pulls/7");

        _ = await finder.FindAsync(JobId.New(), new WorkspaceInfo(new WorkspaceId(Guid.CreateVersion7()), "/worktrees/shop", "avala/shop", "abc1234"), new Reported([.. Enumerable.Repeat(link, 200)], [], []), Cancellation);

        Assert.Equal(1, forge.Reads);
    }

    [Fact]
    public async Task APullRequestLinkOnTheForgesWebHostIsAdoptedWhenOriginIsReachedThroughAnotherSshHostAsync()
    {
        var forge = new CountingForge(PullRequestLifecycle.Open);
        var connections = new ForgeConnections(new Declared(), [forge], new Transports());
        var git = new OriginGit("ssh://git@ssh.forge.example:443/octo/shop.git");
        var finder = new PullRequestFinder(new EffectiveRules(new DeclaredRules(), git, connections), git, connections);

        var found = await finder.FindAsync(
            JobId.New(),
            new WorkspaceInfo(new WorkspaceId(Guid.CreateVersion7()), "/worktrees/shop", "avala/shop", "abc1234"),
            new Reported([new Uri("https://forge.example/octo/shop/pulls/7")], [], []),
            Cancellation);

        Assert.Equal(Option<int>.Some(7), found.Map(delivered => delivered.PullRequest.Number));
    }

    [Fact]
    public async Task APullRequestTheAgentNamesFromABranchThePersonFetchedFromAnotherCloneIsNotAdoptedAsync()
    {
        var forge = new CountingForge(PullRequestLifecycle.Open);
        var connections = new ForgeConnections(new Declared(), [forge], new Transports());
        var git = new FetchedGit();
        var finder = new PullRequestFinder(new EffectiveRules(new DeclaredRules(), git, connections), git, connections);

        var found = await finder.FindAsync(
            JobId.New(),
            new WorkspaceInfo(new WorkspaceId(Guid.CreateVersion7()), "/worktrees/shop", "avala/shop", "f00d123"),
            new Reported([new Uri("https://forge.example/octo/shop/pulls/7")], [], []),
            Cancellation);

        Assert.True(found.IsNone);
    }

    [Theory]
    [InlineData("git push https://forge.example/octo/shop.git HEAD:fix/rounding")]
    [InlineData("git -C /worktrees/shop push --force-with-lease git@forge.example:octo/shop.git +HEAD:refs/heads/fix/rounding")]
    [InlineData("cd /worktrees/shop && git push -u \"https://forge.example/octo/shop.git\" fix/rounding 2>&1")]
    public void ABranchACommandPushedByUrlIsReportedWithTheRepositoryItWentTo(string command) =>
        Assert.Equal([new PushedBranch(Shop, "fix/rounding")], Pushed(command, ItemOutcome.Succeeded));

    [Theory]
    [InlineData("git push origin HEAD:fix/rounding", ItemOutcome.Succeeded)]
    [InlineData("git push https://forge.example/octo/shop.git HEAD:fix/rounding", ItemOutcome.Failed)]
    public void APushToANamedRemoteOrOneThatFailedIsNotReportedAsPushedByUrl(string command, ItemOutcome outcome) =>
        Assert.Empty(Pushed(command, outcome));

    [Theory]
    [InlineData("git push https://forge.example/$OWNER/shop.git HEAD:fix/rounding")]
    [InlineData("git push https://x-access-token:$(cat token)@forge.example/octo/shop.git HEAD:fix/rounding")]
    [InlineData("git push \"https://forge.example/octo/shop.git\" \"HEAD:$BRANCH\"")]
    public void AVariableOutsideTheCredentialsOfAPushUrlLeavesThePushUnreported(string command) =>
        Assert.Empty(Pushed(command, ItemOutcome.Succeeded));

    [Fact]
    public void APushAfterAnOperatorInsideAQuotedArgumentIsNotReportedAsPushedByUrl() =>
        Assert.Empty(Pushed("echo 'done && git push https://forge.example/octo/shop.git HEAD:fix/rounding'", ItemOutcome.Succeeded));

    [Fact]
    public async Task APullRequestPushedByUrlWhoseHeadThisWorktreeNeverHeldIsNotAdoptedAsync() =>
        Assert.True((await FoundAfterAPushByUrlAsync(Shop, held: false)).IsNone);

    [Fact]
    public async Task APullRequestWhoseBranchWasPushedByUrlToAnotherRepositoryIsNotAdoptedAsync() =>
        Assert.True((await FoundAfterAPushByUrlAsync(Shop with { Owner = "mallory" }, held: true)).IsNone);

    [Fact]
    public async Task AJobThatEndsForgetsWhatItsAgentReportedAlsoInTheStoreAsync()
    {
        var store = new Reports();
        await using var book = new ReportedBook(store);
        var job = JobId.New();
        var session = new SessionId(Guid.CreateVersion7());
        var turn = new TurnId(Guid.CreateVersion7());
        await book.StartedAsync(job, session, Cancellation);
        await book.SawAsync(new ItemStarted(session, turn, new ItemId("reply"), ItemKind.Message, "Reply"), Cancellation);
        await book.SawAsync(new ItemProgressed(session, turn, new ItemId("reply"), "Opened https://forge.example/octo/shop/pulls/7"), Cancellation);
        await book.SawAsync(new ItemCompleted(session, turn, new ItemId("reply"), ItemOutcome.Succeeded), Cancellation);
        var kept = store.Kept.Count;

        await book.ForgetAsync(job, Cancellation);

        Assert.Equal((1, 0), (kept, store.Kept.Count));
    }

    private static IReadOnlyList<PushedBranch> Pushed(string command, ItemOutcome outcome)
    {
        var reported = new ReportedLinks();
        var job = JobId.New();
        var session = new SessionId(Guid.CreateVersion7());
        var turn = new TurnId(Guid.CreateVersion7());
        reported.Started(job, session);

        reported.Saw(new ItemStarted(session, turn, new ItemId("push"), ItemKind.Command, $"Run {command}") { Input = command });
        reported.Saw(new ItemCompleted(session, turn, new ItemId("push"), outcome));

        return reported.Of(job).Pushed;
    }

    private static async Task<Option<Delivered>> FoundAfterAPushByUrlAsync(RepositoryAddress pushedTo, bool held)
    {
        var forge = new CountingForge(PullRequestLifecycle.Open);
        var connections = new ForgeConnections(new Declared(), [forge], new Transports());
        var git = new UntrackedGit(held);
        var finder = new PullRequestFinder(new EffectiveRules(new DeclaredRules(), git, connections), git, connections);

        return await finder.FindAsync(
            JobId.New(),
            new WorkspaceInfo(new WorkspaceId(Guid.CreateVersion7()), "/worktrees/shop", "avala/shop", "abc1234"),
            new Reported([new Uri("https://forge.example/octo/shop/pulls/7")], [], [new PushedBranch(pushedTo, "fix/old")]),
            Cancellation);
    }

    private sealed class Reports : IReportedStore
    {
        public Dictionary<JobId, KeptReport> Kept { get; } = [];

        public Task<IReadOnlyList<KeptReport>> ReportsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<KeptReport>>([.. Kept.Values]);

        public Task KeepReportAsync(KeptReport report, CancellationToken cancellationToken)
        {
            Kept[report.Job] = report;

            return Task.CompletedTask;
        }

        public Task ForgetReportAsync(JobId job, CancellationToken cancellationToken)
        {
            _ = Kept.Remove(job);

            return Task.CompletedTask;
        }
    }

    private sealed class UntrackedGit(bool held) : IGitRemote
    {
        public ValueTask<Result<string, ForgeError>> UrlAsync(string worktree, string remote, CancellationToken cancellationToken) => ValueTask.FromResult(Result<string, ForgeError>.Success(Origin));

        public ValueTask<Result<string, ForgeError>> PushAsync(string worktree, string remote, string branch, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<string, ForgeError>> FetchAsync(string worktree, string remote, string branch, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<string>> CheckedOutAsync(string worktree, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<string>>([]);

        public ValueTask<bool> PushedAsync(string worktree, string remote, string branch, string commit, CancellationToken cancellationToken) => ValueTask.FromResult(false);

        public ValueTask<bool> HeldAsync(string worktree, string commit, CancellationToken cancellationToken) => ValueTask.FromResult(held);
    }

    private sealed class FetchedGit : IGitRemote
    {
        public ValueTask<Result<string, ForgeError>> UrlAsync(string worktree, string remote, CancellationToken cancellationToken) => ValueTask.FromResult(Result<string, ForgeError>.Success(Origin));

        public ValueTask<Result<string, ForgeError>> PushAsync(string worktree, string remote, string branch, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<string, ForgeError>> FetchAsync(string worktree, string remote, string branch, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<string>> CheckedOutAsync(string worktree, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<string>>([]);

        public ValueTask<bool> PushedAsync(string worktree, string remote, string branch, string commit, CancellationToken cancellationToken) =>
            ValueTask.FromResult(false);

        public ValueTask<bool> HeldAsync(string worktree, string commit, CancellationToken cancellationToken) => ValueTask.FromResult(false);
    }

    private sealed class CountingForge(PullRequestLifecycle lifecycle = PullRequestLifecycle.Merged) : IForge
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
                new PullRequestState(pullRequest with { Head = "fix/old", Base = "main" }, lifecycle, "abc1234", Mergeability.Unknown) { HeadRepository = context.Target.Repository }));
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

    private sealed class OriginGit(string url = Origin) : IGitRemote
    {
        public ValueTask<Result<string, ForgeError>> UrlAsync(string worktree, string remote, CancellationToken cancellationToken) => ValueTask.FromResult(Result<string, ForgeError>.Success(url));

        public ValueTask<Result<string, ForgeError>> PushAsync(string worktree, string remote, string branch, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<string, ForgeError>> FetchAsync(string worktree, string remote, string branch, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<string>> CheckedOutAsync(string worktree, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<string>>([]);

        public ValueTask<bool> PushedAsync(string worktree, string remote, string branch, string commit, CancellationToken cancellationToken) => ValueTask.FromResult(true);

        public ValueTask<bool> HeldAsync(string worktree, string commit, CancellationToken cancellationToken) => ValueTask.FromResult(true);
    }
}
