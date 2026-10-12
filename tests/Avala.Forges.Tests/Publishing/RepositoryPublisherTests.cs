using Avala.Forges.Connecting;
using Avala.Forges.Contracts;
using Avala.Forges.Policy;
using Avala.Forges.Publishing;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Forges.Tests.Publishing;

public sealed class RepositoryPublisherTests
{
    private const string Repository = "/home/ada/code/shop";

    private static readonly ForgeName Hub = new("hub");

    private static readonly ForgeName Plain = new("plain");

    private readonly HostingForge hosting = new();
    private readonly FakeRemotes remotes = new();
    private readonly FakeRepositories repositories = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OnlyConnectionsWhoseForgeCreatesRepositoriesArePublishTargetsAsync() =>
        Assert.Equal([new PublishTarget(Hub, "Hub")], await Publisher().TargetsAsync(Cancellation));

    [Theory]
    [InlineData(true, "https://hub.example/ada/shop.git")]
    [InlineData(false, "git@hub.example:ada/shop.git")]
    public async Task PublishingCreatesTheRepositoryAndPushesOverHttpsOnlyWhenGitHasACredentialHelperForItAsync(bool helper, string pushed)
    {
        remotes.Helper = helper;

        var published = Outcomes.Succeeds(await Publisher().PublishAsync(Repository, Hub, new RepositoryDraft(" shop ", Private: true), Cancellation));

        Assert.Equal([new RepositoryDraft("shop", Private: true)], hosting.Created);
        Assert.Equal([(Repository, "origin", pushed)], remotes.Published);
        Assert.Equal((new PublishTarget(Hub, "Hub"), "main", pushed, Option<Uri>.Some(new Uri("https://hub.example/ada/shop"))), (published.Forge, published.Branch, published.PushUrl, published.Link));
    }

    [Fact]
    public async Task AnOriginAlreadyOnTheSameForgeAndNameIsOnlyPushedAgainAsync()
    {
        remotes.Origin = "git@hub.example:ada/shop.git";

        _ = Outcomes.Succeeds(await Publisher().PublishAsync(Repository, Hub, new RepositoryDraft("shop", Private: true), Cancellation));

        Assert.Empty(hosting.Created);
        Assert.Equal([(Repository, "origin", "git@hub.example:ada/shop.git")], remotes.Published);
    }

    [Fact]
    public async Task AnOriginElsewhereIsLeftAloneAndNothingIsCreatedAsync()
    {
        remotes.Origin = "git@elsewhere.example:ada/shop.git";

        Assert.Equal(ForgeError.RemoteExists, Outcomes.FailsWith(await Publisher().PublishAsync(Repository, Hub, new RepositoryDraft("shop", Private: true), Cancellation)));
        Assert.Equal((0, 0), (hosting.Created.Count, remotes.Published.Count));
    }

    [Theory]
    [InlineData("plain", RepositoryState.Ready, ForgeError.RepositoriesUnsupported)]
    [InlineData("absent", RepositoryState.Ready, ForgeError.UnknownConnection)]
    [InlineData("hub", RepositoryState.NoCommit, ForgeError.NoBaseBranch)]
    public async Task NothingIsCreatedWhereThereIsNoForgeToCreateOnOrNothingToPushAsync(string connection, RepositoryState state, ForgeError expected)
    {
        repositories.State = state;

        Assert.Equal(expected, Outcomes.FailsWith(await Publisher().PublishAsync(Repository, new ForgeName(connection), new RepositoryDraft("shop", Private: true), Cancellation)));
        Assert.Empty(hosting.Created);
    }

    [Theory]
    [InlineData(WorkspaceFailure.RemoteUnauthorized, ForgeError.PushUnauthorized)]
    [InlineData(WorkspaceFailure.PushRejected, ForgeError.PushRejected)]
    [InlineData(WorkspaceFailure.RemoteUnreachable, ForgeError.Unreachable)]
    public async Task ARefusedPushSaysWhyAfterTheRepositoryWasCreatedAsync(WorkspaceFailure refusal, ForgeError expected)
    {
        remotes.Refusal = refusal;

        Assert.Equal(expected, Outcomes.FailsWith(await Publisher().PublishAsync(Repository, Hub, new RepositoryDraft("shop", Private: true), Cancellation)));
        Assert.Single(hosting.Created);
    }

    [Theory]
    [InlineData("git@hub.example:ada/shop.git", "hub")]
    [InlineData("https://elsewhere.example/ada/shop.git", null)]
    public async Task TheOriginIsReadWithTheConnectionWhoseForgeHostsItAsync(string url, string? connection)
    {
        remotes.Origin = url;

        var remote = Outcomes.Present(await Publisher().RemoteOfAsync(Repository, Cancellation));

        Assert.Equal((url, RemoteAddresses.Parse(url)), (remote.Url, remote.Address));
        Assert.Equal(connection.ToOption().Map(name => new PublishTarget(new ForgeName(name), "Hub")), remote.Forge);
    }

    [Fact]
    public async Task ARepositoryWithoutOriginHasNoRemoteAsync() =>
        Assert.True((await Publisher().RemoteOfAsync(Repository, Cancellation)).IsNone);

    private RepositoryPublisher Publisher() =>
        new(new ForgeConnections(new Declared(), [hosting, new PlainForge()], new Transports()), repositories, remotes);

    private sealed class Declared : IForgeFile
    {
        public ValueTask<Result<ForgeSettings, ForgeError>> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<ForgeSettings, ForgeError>.Success(new ForgeSettings(
                [
                    new ForgeDeclaration(Hub, HostingForge.Id, new Uri("https://api.hub.example"), CredentialSource.None, Option<string>.None),
                    new ForgeDeclaration(Plain, PlainForge.Id, new Uri("https://plain.example"), CredentialSource.None, Option<string>.None),
                ],
                ForgeSettings.DefaultPoll)));
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

    private sealed class PlainForge : IForge
    {
        public const string Id = "plain";

        public ForgeInfo Info { get; } = new(Id, "Plain");

        public Option<CliCall> CliFor(ForgeRequest request, ForgeTarget target) => Option<CliCall>.None;

        public ValueTask<Result<Option<PullRequestRef>, ForgeError>> FindAsync(ForgeContext context, string head, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<PullRequestRef, ForgeError>> OpenAsync(ForgeContext context, PullRequestDraft draft, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<PullRequestState, ForgeError>> ReadAsync(ForgeContext context, PullRequestRef pullRequest, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<CommentRef, ForgeError>> CommentAsync(ForgeContext context, PullRequestRef pullRequest, string body, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class HostingForge : IForge, IForgeRepositories
    {
        public const string Id = "hosting";

        public ForgeInfo Info { get; } = new(Id, "Hub");

        public Option<CliCall> CliFor(ForgeRequest request, ForgeTarget target) => Option<CliCall>.None;

        public ValueTask<Result<Option<PullRequestRef>, ForgeError>> FindAsync(ForgeContext context, string head, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<PullRequestRef, ForgeError>> OpenAsync(ForgeContext context, PullRequestDraft draft, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<PullRequestState, ForgeError>> ReadAsync(ForgeContext context, PullRequestRef pullRequest, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<CommentRef, ForgeError>> CommentAsync(ForgeContext context, PullRequestRef pullRequest, string body, CancellationToken cancellationToken) => throw new NotSupportedException();

        public List<RepositoryDraft> Created { get; } = [];

        public string GitHost(Uri api) => api.Host.Replace("api.", string.Empty, StringComparison.Ordinal);

        public ValueTask<Result<HostedRepository, ForgeError>> CreateAsync(ForgeContext context, RepositoryDraft draft, CancellationToken cancellationToken)
        {
            Created.Add(draft);

            return ValueTask.FromResult(Result<HostedRepository, ForgeError>.Success(new HostedRepository(
                new RepositoryAddress("hub.example", "ada", draft.Name),
                new Uri($"https://hub.example/ada/{draft.Name}"),
                $"https://hub.example/ada/{draft.Name}.git",
                $"git@hub.example:ada/{draft.Name}.git")));
        }
    }

    private sealed class FakeRepositories : IRepositories
    {
        public RepositoryState State { get; set; } = RepositoryState.Ready;

        public ValueTask<RepositoryState> InspectAsync(string folder, CancellationToken cancellationToken) => ValueTask.FromResult(State);

        public ValueTask<Result<string, WorkspaceFailure>> InitializeAsync(string folder, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<string, WorkspaceFailure>> CreateAsync(string parent, string name, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeRemotes : IRemoteRepositories
    {
        public Option<string> Origin { get; set; }

        public bool Helper { get; set; }

        public Option<WorkspaceFailure> Refusal { get; set; }

        public List<(string Repository, string Remote, string Url)> Published { get; } = [];

        public ValueTask<Option<string>> RemoteAsync(string repository, string remote, CancellationToken cancellationToken) => ValueTask.FromResult(Origin);

        public ValueTask<bool> HasCredentialHelperAsync(string repository, string url, CancellationToken cancellationToken) => ValueTask.FromResult(Helper);

        public ValueTask<Result<string, WorkspaceFailure>> PublishAsync(string repository, string remote, string url, CancellationToken cancellationToken)
        {
            Published.Add((repository, remote, url));

            return ValueTask.FromResult(Refusal.Match(Result<string, WorkspaceFailure>.Failure, () => "main"));
        }

        public ValueTask<Result<string, WorkspaceFailure>> CloneAsync(string url, string parent, string name, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
