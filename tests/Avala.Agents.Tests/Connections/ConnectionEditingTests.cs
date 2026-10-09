using Avala.Agents.ConnectionFiles;
using Avala.Agents.Connections;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Agents.Tests.Connections;

public sealed class ConnectionEditingTests
{
    private const string Declared = """{ "default": "work", "connections": [ { "name": "work", "provider": "first", "credential": { "source": "vault", "reference": "work-login" } } ] }""";

    private readonly ScriptedAgentProvider first = new(ScriptedAgentProvider.Reply) { Info = new ProviderInfo("first", "First harness") };

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ADeclaredConnectionJoinsTheCatalogWithItsReferenceAndTheCatalogOffersTheProvidersAndSourcesAsync()
    {
        var registry = Registry(Declared);

        var catalog = Outcomes.Succeeds(await registry.DeclareAsync(
            Option<ConnectionName>.None,
            new ConnectionEdit(new ConnectionName("team"), "first") { Credential = new CredentialReference("vault", " team-login ") },
            Cancellation));

        Assert.Equal(
            [("work", "work-login"), ("team", "team-login")],
            catalog.Connections.Select(connection => (connection.Name.Value, connection.Reference.Match(reference => reference, () => "-"))));
        Assert.Equal(["first"], catalog.Providers.Select(provider => provider.Id));
        Assert.Equal(["vault"], catalog.Sources);
        Assert.Equal(new ConnectionName("team"), Outcomes.Succeeds(await registry.CheckAsync(new ConnectionName("team"), Cancellation)).Name);
    }

    [Fact]
    public async Task RenamingTheDefaultConnectionKeepsItTheDefaultUnderItsNewNameAsync()
    {
        var registry = Registry(Declared);

        var catalog = Outcomes.Succeeds(await registry.DeclareAsync(new ConnectionName("work"), new ConnectionEdit(new ConnectionName("office"), "first"), Cancellation));

        Assert.Equal((Option<ConnectionName>.Some(new ConnectionName("office")), DefaultMode.Fixed), (catalog.Default, catalog.DefaultMode));
        Assert.Equal(["office"], catalog.Connections.Select(connection => connection.Name.Value));
    }

    [Theory]
    [InlineData("auto", "first", "vault", "x", "InvalidName")]
    [InlineData("has space", "first", "vault", "x", "InvalidName")]
    [InlineData("team", "nowhere", "vault", "x", "UnknownProvider")]
    [InlineData("team", "first", "keychain", "x", "UnknownSource")]
    [InlineData("team", "first", "vault", " ", "MissingReference")]
    [InlineData("work", "first", "vault", "x", "DuplicateName")]
    public async Task ADeclarationTheMachineCannotUseIsRefusedBeforeTheFileIsTouchedAsync(string name, string provider, string source, string reference, string refusal)
    {
        var registry = Registry(Declared);

        var refused = await registry.DeclareAsync(
            Option<ConnectionName>.None,
            new ConnectionEdit(new ConnectionName(name), provider) { Credential = new CredentialReference(source, reference) },
            Cancellation);

        Assert.Equal(Enum.Parse<ConnectionError>(refusal), Outcomes.FailsWith(refused));
        Assert.Equal(["work"], (await registry.CatalogAsync(Cancellation)).Connections.Select(connection => connection.Name.Value));
    }

    [Fact]
    public async Task OnlyAConnectionThatIsNotTheDefaultCanBeRemovedAsync()
    {
        var registry = Registry(Declared);
        Outcomes.Succeeds(await registry.DeclareAsync(Option<ConnectionName>.None, new ConnectionEdit(new ConnectionName("team"), "first"), Cancellation));

        Assert.Equal(ConnectionError.RemovesTheDefault, Outcomes.FailsWith(await registry.RemoveAsync(new ConnectionName("work"), Cancellation)));
        var removed = Outcomes.Succeeds(await registry.RemoveAsync(new ConnectionName("team"), Cancellation));

        Assert.Equal(["work"], removed.Connections.Select(connection => connection.Name.Value));
    }

    private ConnectionRegistry Registry(string file) =>
        Connected.Discovering(ConnectionFileParser.Parse(file).Map(Option<ConnectionDeclarations>.Some), [first], [], new Vault());

    private sealed class Vault : ICredentialSource
    {
        public string Source => "vault";

        public ValueTask<Result<ConnectionEnvironment, ConnectionError>> ResolveAsync(CredentialRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<ConnectionEnvironment, ConnectionError>.Success(new ConnectionEnvironment()));
    }
}
