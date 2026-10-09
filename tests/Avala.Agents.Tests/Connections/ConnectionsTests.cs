using Avala.Agents.Connections;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Agents.Sessions;
using Avala.Sdk;
using Avala.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avala.Agents.Tests.Connections;

public sealed class ConnectionsTests
{
    private const string Declared = """
        {
          "default": "personal",
          "connections": [
            { "name": "work", "provider": "first", "credential": { "source": "vault", "reference": "work-login" }, "settings": { "model": "large" } },
            { "name": "personal", "provider": "first", "credential": { "source": "vault", "reference": "personal-login" } },
            { "name": "second", "provider": "second" },
            { "name": "uninstalled", "provider": "missing" },
            { "name": "unsupported", "provider": "first", "credential": { "source": "keychain" } },
            { "name": "broken", "provider": "first", "credential": { "source": "vault" } }
          ]
        }
        """;

    private static readonly AgentRequest Request = new("/worktrees/1");

    private readonly ScriptedAgentProvider first = Provider("first");
    private readonly ScriptedAgentProvider second = Provider("second");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutAConnectionsFileEveryRegisteredProviderHasOneImplicitConnectionAndTheFirstIsTheDefaultAsync()
    {
        var registry = Connected.Registry([first, second]);

        var catalog = await registry.CatalogAsync(Cancellation);

        Assert.Equal((ConnectionFileStatus.Absent, true), (catalog.File, catalog.Error.IsNone));
        Assert.Equal(
            [new DeclaredConnection(new ConnectionName("first"), "first", Option<string>.None), new DeclaredConnection(new ConnectionName("second"), "second", Option<string>.None)],
            catalog.Connections);
        Assert.Equal(Option<ConnectionName>.Some(new ConnectionName("first")), catalog.Default);
        Assert.Equal(new ConnectionInfo(new ConnectionName("first"), first.Info), Outcomes.Succeeds(await registry.CheckAsync(Option<ConnectionName>.None, Cancellation)));
    }

    [Fact]
    public async Task ASessionOpensOnItsNamedConnectionWithTheResolvedEnvironmentAndIsAnnouncedWithItAsync()
    {
        var bus = new RecordingBus();
        await using var agents = Agents(bus, Connected.Registry(Declared, [first, second], new Vault()));

        var opened = Outcomes.Succeeds(await agents.OpenAsync(Request with { Connection = new ConnectionName("work") }, Cancellation));

        var options = Assert.Single(first.Sessions).Options;
        Assert.Equal(Option<string>.Some("/logins/work-login"), options.Connection.ConfigurationDirectory);
        Assert.Equal("large", options.Connection.Settings["model"]);
        Assert.Equal(new ConnectionName("work"), opened.Connection);
        Assert.Equal(new ConnectionName("work"), Assert.Single(bus.Published.OfType<SessionOpened>()).Connection);
    }

    [Fact]
    public async Task ARequestWithoutAConnectionOpensOnTheDeclaredDefaultAsync()
    {
        var bus = new RecordingBus();
        await using var agents = Agents(bus, Connected.Registry(Declared, [second, first], new Vault()));

        var opened = Outcomes.Succeeds(await agents.OpenAsync(Request, Cancellation));

        Assert.Equal(new ConnectionName("personal"), opened.Connection);
        Assert.Equal(Option<string>.Some("/logins/personal-login"), Assert.Single(first.Sessions).Options.Connection.ConfigurationDirectory);
        Assert.Empty(second.Sessions);
    }

    [Fact]
    public async Task TwoConnectionsOfOneProviderOpenSessionsWithTheirOwnEnvironmentAsync()
    {
        await using var agents = Agents(new RecordingBus(), Connected.Registry(Declared, [first], new Vault()));

        Outcomes.Succeeds(await agents.OpenAsync(Request with { Connection = new ConnectionName("work") }, Cancellation));
        Outcomes.Succeeds(await agents.OpenAsync(Request with { Connection = new ConnectionName("personal") }, Cancellation));

        Assert.Equal(
            ["/logins/work-login", "/logins/personal-login"],
            first.Sessions.Select(session => Outcomes.Present(session.Options.Connection.ConfigurationDirectory)));
    }

    [Theory]
    [InlineData("nowhere", "UnknownConnection", "UnknownConnection")]
    [InlineData("uninstalled", "UnknownProvider", "ProviderUnavailable")]
    [InlineData("unsupported", "UnknownSource", "UnusableConnection")]
    [InlineData("broken", "MissingReference", "UnusableConnection")]
    public async Task AConnectionThatCannotBeUsedIsRejectedWithItsReasonAndStartsNothingAsync(string connection, string checkedAs, string openedAs)
    {
        var registry = Connected.Registry(Declared, [first, second], new Vault());
        await using var agents = Agents(new RecordingBus(), registry);

        var check = await registry.CheckAsync(new ConnectionName(connection), Cancellation);
        var open = await agents.OpenAsync(Request with { Connection = new ConnectionName(connection) }, Cancellation);

        Assert.Equal(Enum.Parse<ConnectionError>(checkedAs), Outcomes.FailsWith(check));
        Assert.Equal(Enum.Parse<AgentError>(openedAs), Outcomes.FailsWith(open));
        Assert.Empty(first.Sessions);
        Assert.Empty(second.Sessions);
    }

    [Fact]
    public async Task ARejectedConnectionsFileNeverFallsBackToAnotherConnectionAsync()
    {
        var registry = Connected.Registry("""{ "connections": [ { "name": "work" } ] }""", [first]);
        await using var agents = Agents(new RecordingBus(), registry);

        var catalog = await registry.CatalogAsync(Cancellation);

        Assert.Equal((ConnectionFileStatus.Rejected, Option<ConnectionError>.Some(ConnectionError.MissingProvider), 0), (catalog.File, catalog.Error, catalog.Connections.Count));
        Assert.Equal(AgentError.UnusableConnection, Outcomes.FailsWith(await agents.OpenAsync(Request, Cancellation)));
        Assert.Empty(first.Sessions);
    }

    private static ScriptedAgentProvider Provider(string id) => new(ScriptedAgentProvider.Reply) { Info = new ProviderInfo(id, id) };

    private static AgentSessions Agents(RecordingBus bus, ConnectionRegistry registry) =>
        new(Connected.Starter(registry, [], []), bus, TimeProvider.System, NullLogger<AgentSessions>.Instance);

    private sealed class Vault : ICredentialSource
    {
        public string Source => "vault";

        public ValueTask<Result<ConnectionEnvironment, ConnectionError>> ResolveAsync(CredentialRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(request.Reference.Match(
                login => Result<ConnectionEnvironment, ConnectionError>.Success(new ConnectionEnvironment { ConfigurationDirectory = $"/logins/{login}" }),
                () => ConnectionError.MissingReference));
    }
}
