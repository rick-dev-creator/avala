using Avala.Agents.ConnectionFiles;
using Avala.Agents.Connections;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Agents.Tests.Connections;

public sealed class ConnectionDiscoveryTests
{
    private const string Declared = """
        {
          "default": "personal",
          "connections": [
            { "name": "work", "provider": "first", "credential": { "source": "vault", "reference": "work-login" } },
            { "name": "personal", "provider": "first", "credential": { "source": "vault", "reference": "personal-login" } }
          ]
        }
        """;

    private readonly ScriptedAgentProvider first = new(ScriptedAgentProvider.Reply) { Info = new ProviderInfo("first", "first") };
    private readonly ScriptedAgentProvider second = new(ScriptedAgentProvider.Reply) { Info = new ProviderInfo("second", "second") };

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutAConnectionsFileTheDiscoveredConnectionsOfAProviderReplaceItsImplicitOneAndTheFirstIsTheDefaultAsync()
    {
        var discovery = new FixedDiscovery(Found("first-a", "a-login"), Found("first-b", "b-login"));
        var registry = Connected.Discovering(Absent, [first, second], [discovery], new Vault());

        var catalog = await registry.CatalogAsync(Cancellation);

        Assert.Equal(
            [("first-a", "first", ConnectionOrigin.Discovered), ("first-b", "first", ConnectionOrigin.Discovered), ("second", "second", ConnectionOrigin.Implicit)],
            catalog.Connections.Select(connection => (connection.Name.Value, connection.Provider, connection.Origin)));
        Assert.Equal((ConnectionFileStatus.Absent, Option<ConnectionName>.Some(new ConnectionName("first-a")), DefaultMode.Auto), (catalog.File, catalog.Default, catalog.DefaultMode));
    }

    [Fact]
    public async Task DeclaredConnectionsWinAndADiscoveredOneWithTheirNameOrCredentialIsLeftOutAsync()
    {
        var discovery = new FixedDiscovery(Found("work", "elsewhere"), Found("personal-copy", "personal-login"), Found("first-c", "c-login"));
        var registry = Connected.Discovering(Parsed(Declared), [first], [discovery], new Vault());

        var catalog = await registry.CatalogAsync(Cancellation);

        Assert.Equal(
            [("work", ConnectionOrigin.Declared), ("personal", ConnectionOrigin.Declared), ("first-c", ConnectionOrigin.Discovered)],
            catalog.Connections.Select(connection => (connection.Name.Value, connection.Origin)));
        Assert.Equal(Option<ConnectionName>.Some(new ConnectionName("personal")), catalog.Default);
        Assert.Equal(new ConnectionInfo(new ConnectionName("work"), first.Info) { Capabilities = first.CapabilitiesOn(ConnectionEnvironment.Default) }, Outcomes.Succeeds(await registry.CheckAsync(new ConnectionName("work"), Cancellation)));
    }

    [Fact]
    public async Task ARejectedConnectionsFileLeavesTheDiscoveredConnectionsUnusableAsync()
    {
        var discovery = new FixedDiscovery(Found("first-a", "a-login"));
        var registry = Connected.Discovering(ConnectionError.UnknownField, [first], [discovery], new Vault());

        var catalog = await registry.CatalogAsync(Cancellation);

        Assert.Equal((ConnectionFileStatus.Rejected, 0), (catalog.File, catalog.Connections.Count));
        Assert.Equal(ConnectionError.UnknownField, Outcomes.FailsWith(await registry.CheckAsync(new ConnectionName("first-a"), Cancellation)));
        Assert.Equal(ConnectionError.UnknownField, Outcomes.FailsWith(await registry.CheckAsync(Option<ConnectionName>.None, Cancellation)));
    }

    [Fact]
    public async Task ADiscoveredConnectionOpensWithTheEnvironmentItsReferenceResolvesToAndItsSettingsAsync()
    {
        var discovery = new FixedDiscovery(Found("first-a", "a-login") with { Settings = new Dictionary<string, string> { ["model"] = "large" } });
        var registry = Connected.Discovering(Absent, [first], [discovery], new Vault());

        var resolved = Outcomes.Succeeds(await registry.ResolveAsync(new ConnectionName("first-a"), Cancellation));

        Assert.Equal((Option<string>.Some("/logins/a-login"), "large"), (resolved.Environment.ConfigurationDirectory, resolved.Environment.Settings["model"]));
    }

    [Fact]
    public async Task ADiscoveredConnectionWithAnInvalidNameAnUnregisteredProviderABlankReferenceOrARepeatedNameIsLeftOutAsync()
    {
        var discovery = new FixedDiscovery(
            Found("-invalid", "x-login"),
            Found("unregistered", "u-login") with { Provider = "missing" },
            Found("blank", " "),
            Found("first-a", "a-login"),
            Found("first-a", "other-login"),
            Found("first-again", "a-login"));
        var registry = Connected.Discovering(Absent, [first], [discovery], new Vault());

        var catalog = await registry.CatalogAsync(Cancellation);

        Assert.Equal(["first-a"], catalog.Connections.Select(connection => connection.Name.Value));
    }

    [Fact]
    public async Task DiscoveryRunsOnceSoTheConnectionsStayTheSameWhileTheApplicationRunsAsync()
    {
        var discovery = new FixedDiscovery(Found("first-a", "a-login"));
        var registry = Connected.Discovering(Absent, [first], [discovery], new Vault());

        var before = await registry.CatalogAsync(Cancellation);
        discovery.Connections = [Found("first-b", "b-login")];
        _ = await registry.CheckAsync(Option<ConnectionName>.None, Cancellation);
        var after = await registry.CatalogAsync(Cancellation);

        Assert.Equal(before.Connections, after.Connections);
        Assert.Equal(1, discovery.Calls);
    }

    private static Result<Option<ConnectionDeclarations>, ConnectionError> Absent => Option<ConnectionDeclarations>.None;

    private static Result<Option<ConnectionDeclarations>, ConnectionError> Parsed(string file) =>
        ConnectionFileParser.Parse(file).Map(Option<ConnectionDeclarations>.Some);

    private static DiscoveredConnection Found(string name, string reference) =>
        new(new ConnectionName(name), "first", new CredentialReference("vault", reference));

    private sealed class FixedDiscovery(params DiscoveredConnection[] found) : IConnectionDiscovery
    {
        public IReadOnlyList<DiscoveredConnection> Connections { get; set; } = found;

        public int Calls { get; private set; }

        public ValueTask<IReadOnlyList<DiscoveredConnection>> DiscoverAsync(CancellationToken cancellationToken)
        {
            Calls++;

            return ValueTask.FromResult(Connections);
        }
    }

    private sealed class Vault : ICredentialSource
    {
        public string Source => "vault";

        public ValueTask<Result<ConnectionEnvironment, ConnectionError>> ResolveAsync(CredentialRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(request.Reference.Match(
                login => Result<ConnectionEnvironment, ConnectionError>.Success(new ConnectionEnvironment { ConfigurationDirectory = $"/logins/{login}" }),
                () => ConnectionError.MissingReference));
    }
}
