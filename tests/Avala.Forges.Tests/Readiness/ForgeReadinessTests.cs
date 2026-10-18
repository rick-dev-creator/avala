using Avala.Forges.Contracts;
using Avala.Forges.Policy;
using Avala.Forges.Readiness;
using Avala.Forges.Tests.Accounts;
using Avala.Sdk;

namespace Avala.Forges.Tests.Readiness;

public sealed class ForgeReadinessTests
{
    private const string Repository = "/home/ada/code/shop";

    private static readonly SignInTool HubCli = new("hub", [KnownHub.Id], "hub auth login", Installed: true);

    private readonly FakePublishing publishing = new();
    private readonly List<AccountStatus> accounts = [];
    private readonly List<SignInTool> tools = [HubCli];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnOriginHostedByAUsableConnectionIsReadyThroughItAsync()
    {
        publishing.Origin = Remote("git@hub.example:ada/shop.git") with { Forge = new PublishTarget(new ForgeName("hub"), "Hub") };

        var ready = await Readiness().OfAsync(Repository, Cancellation);

        Assert.Equal((ForgeReadinessKind.Hosted, "hub.example", Option<PublishTarget>.Some(new PublishTarget(new ForgeName("hub"), "Hub"))), (ready.Kind, ready.Host, ready.Connection));
    }

    [Fact]
    public async Task AnOriginWithoutConnectionOffersEveryDetectedLoginForItsHostAndNoOtherAsync()
    {
        publishing.Origin = Remote("https://hub.example/ada/shop.git");
        accounts.AddRange([Detected("hub", "hub.example", "octocat"), Detected("hub-2", "hub.example", "ada"), Detected("cup", "cup.example", "ada")]);

        var ready = await Readiness().OfAsync(Repository, Cancellation);

        Assert.Equal((ForgeReadinessKind.Connectable, "hub.example", "Hub"), (ready.Kind, ready.Host, ready.Forge.Match(forge => forge.Name, () => string.Empty)));
        Assert.Equal(["hub", "hub-2"], ready.Accounts.Select(account => account.Name.Value));
        Assert.Equal(Option<SignInTool>.Some(HubCli), ready.SignIn);
    }

    [Fact]
    public async Task AnOriginOnAForgesHostWithoutAnyLoginAsksToSignInWithThatForgesToolAsync()
    {
        publishing.Origin = Remote("https://hub.example/ada/shop.git");

        var ready = await Readiness().OfAsync(Repository, Cancellation);

        Assert.Equal((ForgeReadinessKind.SignInNeeded, "hub.example", Option<SignInTool>.Some(HubCli)), (ready.Kind, ready.Host, ready.SignIn));
        Assert.Empty(ready.Accounts);
    }

    [Theory]
    [InlineData(null, "NoRemote")]
    [InlineData("/srv/git/shop.git", "Unknown")]
    [InlineData("https://elsewhere.example/ada/shop.git", "Unknown")]
    [InlineData("https://api.cup.example/ada/shop.git", "Unknown")]
    public async Task NoOriginALocalPathOrAHostNoForgeSignsInToHasNoForgeToConnectAsync(string? origin, string kind)
    {
        publishing.Origin = origin.ToOption().Map(Remote);

        var ready = await Readiness().OfAsync(Repository, Cancellation);

        Assert.Equal(Enum.Parse<ForgeReadinessKind>(kind), ready.Kind);
    }

    [Fact]
    public async Task ATokenLoginWithoutAnAddressHostsNoLocalPathAsync()
    {
        publishing.Origin = Remote("/srv/git/shop.git");
        accounts.Add(new AccountStatus(new ForgeName("tea"), "cup", string.Empty, CredentialSource.Environment, Option<ForgeAccount>.None, Option<ForgeError>.None, AccountOrigin.Detected) { Reference = "CUP_TOKEN" });

        var ready = await Readiness().OfAsync(Repository, Cancellation);

        Assert.Equal((ForgeReadinessKind.Unknown, 0), (ready.Kind, ready.Accounts.Count));
    }

    private static RepositoryRemote Remote(string url) => new("origin", url) { Address = RemoteAddresses.Parse(url) };

    private static AccountStatus Detected(string name, string host, string login) =>
        new(new ForgeName(name), KnownHub.Id, host, CredentialSource.Cli, new ForgeAccount(login), Option<ForgeError>.None, AccountOrigin.Detected) { Url = new Uri($"https://{host}") };

    private ForgeReadinessDesk Readiness() =>
        new(publishing, new FakeAccounts(accounts, tools), [new KnownHub(), new KindOnly("cup", new Uri("https://api.cup.example"))]);

    private sealed class KnownHub : IForge, IForgeRepositories
    {
        public const string Id = "hub";

        public ForgeInfo Info { get; } = new(Id, "Hub") { DefaultUrl = new Uri("https://api.hub.example") };

        public string GitHost(Uri api) => api.Host.Replace("api.", string.Empty, StringComparison.Ordinal);

        public Option<CliCall> CliFor(ForgeRequest request, ForgeTarget target) => Option<CliCall>.None;

        public ValueTask<Result<Option<PullRequestRef>, ForgeError>> FindAsync(ForgeContext context, string head, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<PullRequestRef, ForgeError>> OpenAsync(ForgeContext context, PullRequestDraft draft, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<PullRequestState, ForgeError>> ReadAsync(ForgeContext context, PullRequestRef pullRequest, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<CommentRef, ForgeError>> CommentAsync(ForgeContext context, PullRequestRef pullRequest, string body, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<HostedRepository, ForgeError>> CreateAsync(ForgeContext context, RepositoryDraft draft, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<IReadOnlyList<ListedRepository>, ForgeError>> ListAsync(ForgeContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakePublishing : IRepositoryPublishing
    {
        public Option<RepositoryRemote> Origin { get; set; }

        public ValueTask<IReadOnlyList<PublishTarget>> TargetsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Option<RepositoryRemote>> RemoteOfAsync(string repository, CancellationToken cancellationToken) => ValueTask.FromResult(Origin);

        public ValueTask<Result<PublishedRepository, ForgeError>> PublishAsync(string repository, ForgeName forge, RepositoryDraft draft, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeAccounts(IReadOnlyList<AccountStatus> accounts, IReadOnlyList<SignInTool> tools) : ISourceControlAccounts
    {
        public ValueTask<SourceControlAccounts> AccountsAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new SourceControlAccounts(accounts, tools));

        public ValueTask<Result<IReadOnlyList<RemoteRepository>, ForgeError>> RepositoriesAsync(ForgeName forge, string search, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
