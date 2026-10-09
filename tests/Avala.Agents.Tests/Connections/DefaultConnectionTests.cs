using Avala.Agents.ConnectionFiles;
using Avala.Agents.Connections;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Agents.Tests.Connections;

public sealed class DefaultConnectionTests
{
    private readonly ScriptedAgentProvider first = new(ScriptedAgentProvider.Reply) { Info = new ProviderInfo("first", "first") };
    private readonly FixedDiscovery discovery = new(Found("first-a"), Found("first-b"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static ConnectionName FirstB => new("first-b");

    [Fact]
    public async Task AFileHoldingOnlyTheDefaultKeepsTheDiscoveredConnectionsAndFixesTheDefaultOnOneOfThemAsync()
    {
        var registry = Registry(Parsed("""{ "default": "first-b" }"""));

        var catalog = await registry.CatalogAsync(Cancellation);

        Assert.Equal((ConnectionFileStatus.Applied, DefaultMode.Fixed, Option<ConnectionName>.Some(FirstB)), (catalog.File, catalog.DefaultMode, catalog.Default));
        Assert.Equal([ConnectionOrigin.Discovered, ConnectionOrigin.Discovered], catalog.Connections.Select(connection => connection.Origin));
        Assert.Equal(FirstB, Outcomes.Succeeds(await registry.CheckAsync(Option<ConnectionName>.None, Cancellation)).Name);
    }

    [Fact]
    public async Task ADefaultNamingADiscoveredConnectionThatIsGoneRejectsTheFileAndLeavesEveryConnectionUnusableAsync()
    {
        var registry = Registry(Parsed("""{ "default": "first-c" }"""));

        var catalog = await registry.CatalogAsync(Cancellation);

        Assert.Equal((ConnectionFileStatus.Rejected, Option<ConnectionError>.Some(ConnectionError.UnknownDefault), 0), (catalog.File, catalog.Error, catalog.Connections.Count));
        Assert.Equal(ConnectionError.UnknownDefault, Outcomes.FailsWith(await registry.CheckAsync(new ConnectionName("first-a"), Cancellation)));
    }

    [Fact]
    public async Task ChangingTheDefaultToAKnownConnectionFixesItAtOnceAndAutoReleasesItAsync()
    {
        var registry = Registry(Option<ConnectionDeclarations>.None);

        var fixedOne = Outcomes.Succeeds(await registry.ChangeDefaultAsync(FirstB, Cancellation));
        var opensOn = Outcomes.Succeeds(await registry.CheckAsync(Option<ConnectionName>.None, Cancellation)).Name;
        var automatic = Outcomes.Succeeds(await registry.ChangeDefaultAsync(Option<ConnectionName>.None, Cancellation));

        Assert.Equal((DefaultMode.Fixed, Option<ConnectionName>.Some(FirstB), FirstB), (fixedOne.DefaultMode, fixedOne.Default, opensOn));
        Assert.Equal((DefaultMode.Auto, Option<ConnectionName>.Some(new ConnectionName("first-a"))), (automatic.DefaultMode, automatic.Default));
    }

    [Theory]
    [InlineData("nowhere", "UnknownConnection")]
    [InlineData("auto", "InvalidName")]
    public async Task AnUnknownOrReservedNameIsRefusedAndTheDefaultStaysAsItWasAsync(string name, string refusal)
    {
        var registry = Registry(Parsed("""{ "default": "first-b" }"""));

        var refused = await registry.ChangeDefaultAsync(new ConnectionName(name), Cancellation);

        Assert.Equal(Enum.Parse<ConnectionError>(refusal), Outcomes.FailsWith(refused));
        Assert.Equal(Option<ConnectionName>.Some(FirstB), (await registry.CatalogAsync(Cancellation)).Default);
    }

    [Fact]
    public async Task ChoosingAgainRepairsADefaultWhoseConnectionIsGoneAsync()
    {
        var registry = Registry(Parsed("""{ "default": "first-c" }"""));

        var repaired = Outcomes.Succeeds(await registry.ChangeDefaultAsync(Option<ConnectionName>.None, Cancellation));

        Assert.Equal((ConnectionFileStatus.Applied, DefaultMode.Auto, 2), (repaired.File, repaired.DefaultMode, repaired.Connections.Count));
    }

    [Fact]
    public async Task AFileRejectedForAnotherReasonRefusesTheChangeWithThatReasonAsync()
    {
        var registry = Registry(ConnectionError.UnknownField);

        Assert.Equal(ConnectionError.UnknownField, Outcomes.FailsWith(await registry.ChangeDefaultAsync(FirstB, Cancellation)));
    }

    private ConnectionRegistry Registry(Result<Option<ConnectionDeclarations>, ConnectionError> file) =>
        Connected.Discovering(file, [first], [discovery], new Vault());

    private static Result<Option<ConnectionDeclarations>, ConnectionError> Parsed(string file) =>
        ConnectionFileParser.Parse(file).Map(Option<ConnectionDeclarations>.Some);

    private static DiscoveredConnection Found(string name) =>
        new(new ConnectionName(name), "first", new CredentialReference("vault", $"{name}-login"));

    private sealed class FixedDiscovery(params DiscoveredConnection[] found) : IConnectionDiscovery
    {
        public ValueTask<IReadOnlyList<DiscoveredConnection>> DiscoverAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<DiscoveredConnection>>(found);
    }

    private sealed class Vault : ICredentialSource
    {
        public string Source => "vault";

        public ValueTask<Result<ConnectionEnvironment, ConnectionError>> ResolveAsync(CredentialRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<ConnectionEnvironment, ConnectionError>.Success(new ConnectionEnvironment()));
    }
}
