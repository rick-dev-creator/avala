using Avala.Forges.Accounts;
using Avala.Forges.Connecting;
using Avala.Forges.Contracts;
using Avala.Forges.Policy;
using Avala.Forges.Tests.Publishing;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Forges.Tests.Accounts;

public sealed class RemoteRepositoriesTests
{
    private static readonly ForgeName Hub = new("hub");

    private static readonly ForgeName Plain = new("plain");

    private readonly HostingForge hosting = new();
    private readonly Helpers helpers = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAccountsRepositoriesAreListedMostRecentlyUpdatedFirstAndClonedOverHttpsOnlyWhereGitHasACredentialHelperAsync()
    {
        helpers.Sites.Add("https://hub.example/");
        hosting.Listed.AddRange([Listed("hub.example", "ada", "ledger", 1, archived: true), Listed("hub.example", "ada", "shop", 9), Listed("mirror.example", "acme", "site", 5)]);

        var listed = Outcomes.Succeeds(await Accounts().RepositoriesAsync(Hub, string.Empty, Cancellation));

        Assert.Equal(
            [("shop", "https://hub.example/ada/shop.git", false), ("site", "git@mirror.example:acme/site.git", false), ("ledger", "https://hub.example/ada/ledger.git", true)],
            listed.Select(found => (found.Address.Name, found.CloneUrl, found.Archived)));
        Assert.Equal(["https://hub.example/", "https://mirror.example/"], helpers.Asked);
    }

    [Theory]
    [InlineData("SHOP", new[] { "ada/shop" })]
    [InlineData("acme/", new[] { "acme/site" })]
    [InlineData("  ", new[] { "ada/shop", "acme/site" })]
    public async Task ASearchKeepsTheRepositoriesWhoseOwnerOrNameHoldItAsync(string search, string[] expected)
    {
        hosting.Listed.AddRange([Listed("hub.example", "ada", "shop", 9), Listed("hub.example", "acme", "site", 5)]);

        var listed = Outcomes.Succeeds(await Accounts().RepositoriesAsync(Hub, search, Cancellation));

        Assert.Equal(expected, listed.Select(found => $"{found.Address.Owner}/{found.Address.Name}"));
    }

    [Theory]
    [InlineData("plain", ForgeError.RepositoriesUnsupported)]
    [InlineData("absent", ForgeError.UnknownConnection)]
    public async Task AnAccountWithoutRepositoriesToListSaysWhyAsync(string connection, ForgeError expected) =>
        Assert.Equal(expected, Outcomes.FailsWith(await Accounts().RepositoriesAsync(new ForgeName(connection), string.Empty, Cancellation)));

    [Fact]
    public async Task ADeclaredAccountNamesWhoItSignsInAsOrWhyItCannotAsync()
    {
        var signedIn = Assert.Single((await Accounts().AccountsAsync(Cancellation)).Accounts, account => account.Name == Hub).Account;
        hosting.Me = ForgeError.Unauthorized;

        var refused = Assert.Single((await Accounts().AccountsAsync(Cancellation)).Accounts, account => account.Name == Hub);

        Assert.Equal(Option<ForgeAccount>.Some(new ForgeAccount("ada")), signedIn);
        Assert.Equal((Option<ForgeAccount>.None, Option<ForgeError>.Some(ForgeError.Unauthorized)), (refused.Account, refused.Problem));
    }

    private static ListedRepository Listed(string host, string owner, string name, int day, bool archived = false) =>
        new(
            new HostedRepository(new RepositoryAddress(host, owner, name), new Uri($"https://{host}/{owner}/{name}"), $"https://{host}/{owner}/{name}.git", $"git@{host}:{owner}/{name}.git"),
            Private: true,
            archived,
            new DateTimeOffset(2026, 10, day, 8, 0, 0, TimeSpan.Zero));

    private SourceControlDesk Accounts()
    {
        IForge[] kinds = [hosting, new KindOnly("plain", new Uri("https://plain.example"))];
        var connections = new ForgeConnections(new Declared(), kinds, new Transports());

        return new SourceControlDesk(connections, kinds, new NoDetection(), new CloneUrls(helpers, new AvalaPaths("/data")));
    }

    private sealed class Declared : IForgeFile
    {
        public ValueTask<Result<ForgeSettings, ForgeError>> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<ForgeSettings, ForgeError>.Success(new ForgeSettings(
                [
                    new ForgeDeclaration(Hub, HostingForge.Id, new Uri("https://api.hub.example"), CredentialSource.None, Option<string>.None),
                    new ForgeDeclaration(Plain, "plain", Option<Uri>.None, CredentialSource.None, Option<string>.None),
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

    private sealed class NoDetection : ILoginDetection
    {
        public ValueTask<DetectedAccounts> DetectAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new DetectedAccounts([], []));
    }

    private sealed class Helpers : IRemoteRepositories
    {
        public HashSet<string> Sites { get; } = new(StringComparer.Ordinal);

        public List<string> Asked { get; } = [];

        public ValueTask<Option<string>> RemoteAsync(string repository, string remote, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<bool> HasCredentialHelperAsync(string repository, string url, CancellationToken cancellationToken)
        {
            Asked.Add(url);

            return ValueTask.FromResult(Sites.Contains(url));
        }

        public ValueTask<Result<string, WorkspaceFailure>> PublishAsync(string repository, string remote, string url, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<string, WorkspaceFailure>> CloneAsync(string url, string parent, string name, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
