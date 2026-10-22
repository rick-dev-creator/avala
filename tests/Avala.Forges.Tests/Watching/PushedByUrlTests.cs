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

public sealed class PushedByUrlTests
{
    private const string Origin = "https://forge.example/octo/shop.git";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("git push https://forge.example/octo/shop.git HEAD:fix/rounding || true")]
    [InlineData("git push https://forge.example/octo/shop.git HEAD:fix/rounding; echo done")]
    [InlineData("git push https://forge.example/octo/shop.git HEAD:fix/rounding 2>&1 | tail -n 3")]
    [InlineData("git push https://forge.example/octo/shop.git HEAD:fix/rounding || git push https://forge.example/octo/shop.git HEAD:avala/mine")]
    public void ABranchAPushThatMayHaveFailedNamedIsNotReportedAsPushedByUrlWhenTheCommandStillSucceeds(string command) =>
        Assert.DoesNotContain(Pushed(command).Select(pushed => pushed.Branch), branch => branch == "fix/rounding");

    [Theory]
    [InlineData("git commit -m \"Next: git push https://forge.example/octo/shop.git HEAD:fix/rounding\"")]
    [InlineData("echo 'git push https://forge.example/octo/shop.git HEAD:fix/rounding'")]
    public void APushOnlyNamedInsideAQuotedArgumentIsNotReportedAsPushedByUrl(string command) =>
        Assert.Empty(Pushed(command));

    [Theory]
    [InlineData("git push --dry-run https://forge.example/octo/shop.git HEAD:fix/rounding")]
    [InlineData("git push -n https://forge.example/octo/shop.git HEAD:fix/rounding")]
    [InlineData("git push --delete https://forge.example/octo/shop.git fix/rounding")]
    [InlineData("git push -d https://forge.example/octo/shop.git fix/rounding")]
    [InlineData("git push https://forge.example/octo/shop.git :fix/rounding")]
    public void APushThatLeavesNoCommitOnTheBranchIsNotReportedAsPushedByUrl(string command) =>
        Assert.Empty(Pushed(command));

    [Fact]
    public void APullRequestLinkWithCredentialsInItIsKeptWithoutThem()
    {
        var reported = new ReportedLinks();
        var job = JobId.New();
        var session = new SessionId(Guid.CreateVersion7());
        var turn = new TurnId(Guid.CreateVersion7());
        reported.Started(job, session);

        reported.Saw(new ItemStarted(session, turn, new ItemId("reply"), ItemKind.Message, "Reply"));
        reported.Saw(new ItemProgressed(session, turn, new ItemId("reply"), "Opened https://x-access-token:ghp_secret@forge.example/octo/shop/pulls/7 "));
        reported.Saw(new ItemCompleted(session, turn, new ItemId("reply"), ItemOutcome.Succeeded));

        Assert.All(reported.Of(job).Links, link => Assert.Empty(link.UserInfo));
    }

    [Fact]
    public async Task APullRequestAnMcpToolCreatedIsAdoptedWhenItsBodyLinksOtherPullRequestsOfTheRepositoryAsync()
    {
        var reported = new ReportedLinks();
        var job = JobId.New();
        var session = new SessionId(Guid.CreateVersion7());
        var turn = new TurnId(Guid.CreateVersion7());
        reported.Started(job, session);
        var others = string.Join(", ", Enumerable.Range(1, 5).Select(number => $"https://forge.example/octo/shop/pulls/{number}"));
        reported.Saw(new ItemStarted(session, turn, new ItemId("create"), ItemKind.Mcp, "mcp__forge__create_pull_request"));
        reported.Saw(new ItemProgressed(session, turn, new ItemId("create"), $$"""{"html_url": "https://forge.example/octo/shop/pulls/7", "body": "Supersedes {{others}}."}"""));
        reported.Saw(new ItemCompleted(session, turn, new ItemId("create"), ItemOutcome.Succeeded));
        var forge = new NumberedForge();
        var connections = new ForgeConnections(new Declared(), [forge], new Transports());
        var git = new SevenPushedGit();
        var finder = new PullRequestFinder(new EffectiveRules(new DeclaredRules(), git, connections), git, connections);

        var found = await finder.FindAsync(job, new WorkspaceInfo(new WorkspaceId(Guid.CreateVersion7()), "/worktrees/shop", "avala/shop", "abc1234"), reported.Of(job), Cancellation);

        Assert.Equal(Option<int>.Some(7), found.Map(delivered => delivered.PullRequest.Number));
    }

    private static IReadOnlyList<PushedBranch> Pushed(string command)
    {
        var reported = new ReportedLinks();
        var job = JobId.New();
        var session = new SessionId(Guid.CreateVersion7());
        var turn = new TurnId(Guid.CreateVersion7());
        reported.Started(job, session);

        reported.Saw(new ItemStarted(session, turn, new ItemId("push"), ItemKind.Command, $"Run {command}") { Input = command });
        reported.Saw(new ItemCompleted(session, turn, new ItemId("push"), ItemOutcome.Succeeded));

        return reported.Of(job).Pushed;
    }

    private sealed class NumberedForge : IForge
    {
        public const string Id = "numbered";

        public ForgeInfo Info { get; } = new(Id, "Numbered");

        public Option<CliCall> CliFor(ForgeRequest request, ForgeTarget target) => Option<CliCall>.None;

        public ValueTask<Result<Option<PullRequestRef>, ForgeError>> FindAsync(ForgeContext context, string head, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<Option<PullRequestRef>, ForgeError>.Success(Option<PullRequestRef>.None));

        public ValueTask<Result<PullRequestRef, ForgeError>> OpenAsync(ForgeContext context, PullRequestDraft draft, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<PullRequestState, ForgeError>> ReadAsync(ForgeContext context, PullRequestRef pullRequest, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<PullRequestState, ForgeError>.Success(
                new PullRequestState(pullRequest with { Head = $"fix/{pullRequest.Number}", Base = "main" }, PullRequestLifecycle.Open, $"c0ffee{pullRequest.Number}", Mergeability.Unknown) { HeadRepository = context.Target.Repository }));

        public ValueTask<Result<CommentRef, ForgeError>> CommentAsync(ForgeContext context, PullRequestRef pullRequest, string body, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class SevenPushedGit : IGitRemote
    {
        public ValueTask<Result<string, ForgeError>> UrlAsync(string worktree, string remote, CancellationToken cancellationToken) => ValueTask.FromResult(Result<string, ForgeError>.Success(Origin));

        public ValueTask<Result<string, ForgeError>> PushAsync(string worktree, string remote, string branch, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<string, ForgeError>> FetchAsync(string worktree, string remote, string branch, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<string>> CheckedOutAsync(string worktree, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<string>>([]);

        public ValueTask<bool> PushedAsync(string worktree, string remote, string branch, string commit, CancellationToken cancellationToken) =>
            ValueTask.FromResult(branch == "fix/7" && commit == "c0ffee7");

        public ValueTask<bool> HeldAsync(string worktree, string commit, CancellationToken cancellationToken) => ValueTask.FromResult(commit == "c0ffee7");
    }

    private sealed class Declared : IForgeFile
    {
        public ValueTask<Result<ForgeSettings, ForgeError>> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<ForgeSettings, ForgeError>.Success(new ForgeSettings(
                [new ForgeDeclaration(new ForgeName("work"), NumberedForge.Id, new Uri("https://forge.example/api"), CredentialSource.None, Option<string>.None)],
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
}
