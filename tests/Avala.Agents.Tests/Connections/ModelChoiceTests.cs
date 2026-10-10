using System.Text.Json.Nodes;
using Avala.Agents.ConnectionFiles;
using Avala.Agents.Connections;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Agents.Tests.Connections;

public sealed class ModelChoiceTests
{
    private static readonly OffersModels Offered = new(["large", "small"], ["low", "high"]);

    private readonly List<SessionOptions> launched = [];
    private readonly ScriptedAgentProvider choosing;
    private readonly ScriptedAgentProvider plain;

    public ModelChoiceTests()
    {
        choosing = new ScriptedAgentProvider(ScriptedAgentProvider.Reply)
        {
            Info = new ProviderInfo("choosing", "Choosing"),
            OnConnection = (connection, declared) => declared.With(Offered.WithDefaults(OffersModels.SettingsOf(connection.Settings))),
            Launching = launched.Add,
        };
        plain = new ScriptedAgentProvider(ScriptedAgentProvider.Reply) { Info = new ProviderInfo("plain", "Plain"), Launching = launched.Add };
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("""{ "model": "large", "effort": "high" }""", "")]
    [InlineData("""{ "model": "huge" }""", "UnofferedModel")]
    [InlineData("""{ "model": "small", "effort": "max" }""", "UnofferedEffort")]
    public async Task AConnectionWhoseSettingsNameAModelOrEffortTheHarnessDoesNotOfferIsRefusedWhenResolvedAndMarkedInTheCatalogAsync(string settings, string refusal)
    {
        var registry = Registry(settings);

        var checkedOne = await registry.CheckAsync(new ConnectionName("work"), Cancellation);
        var catalog = await registry.CatalogAsync(Cancellation);

        Assert.Equal(refusal, checkedOne.Match(_ => string.Empty, error => error.ToString()));
        Assert.Equal(refusal, catalog.Connections[0].Problem.Match(error => error.ToString(), () => string.Empty));
    }

    [Fact]
    public async Task ACheckedConnectionCarriesTheComponentWithItsSettingsAsDefaultsAsync()
    {
        var checkedOne = Outcomes.Succeeds(await Registry("""{ "model": "large" }""").CheckAsync(new ConnectionName("work"), Cancellation));

        Assert.Equal(Offered with { DefaultModel = "large" }, Outcomes.Present(checkedOne.Capabilities.Get<OffersModels>()));
    }

    [Fact]
    public async Task AConnectionWithAnUnofferedModelNeverStartsTheHarnessAsync()
    {
        var starter = Connected.Starter(Registry("""{ "model": "huge" }"""), [], []);

        var refused = await starter.StartAsync(new AgentRequest("/work") { Connection = new ConnectionName("work") }, Cancellation);

        Assert.Equal(AgentError.UnofferedModel, Outcomes.FailsWith(refused));
        Assert.Empty(launched);
    }

    [Fact]
    public async Task AnOfferedChoiceTravelsToTheProviderInTheSessionOptionsAsync()
    {
        var starter = Connected.Starter(Registry("{}"), [], []);

        await using var started = Outcomes.Succeeds(await starter.StartAsync(
            new AgentRequest("/work") { Connection = new ConnectionName("work"), Model = new ModelChoice("large", "low") },
            Cancellation)).Session;

        Assert.Equal(new ModelChoice("large", "low"), Assert.Single(launched).Model);
    }

    [Theory]
    [InlineData("work", "huge", "", "UnofferedModel")]
    [InlineData("work", "large", "max", "UnofferedEffort")]
    [InlineData("other", "large", "", "UnofferedModel")]
    [InlineData("other", "", "high", "UnofferedEffort")]
    public async Task AChoiceTheConnectionDoesNotOfferIsRefusedWithoutStartingTheHarnessAsync(string connection, string model, string effort, string refusal)
    {
        var starter = Connected.Starter(Registry("{}"), [], []);
        var choice = new ModelChoice(model.Length == 0 ? Option<string>.None : model, effort.Length == 0 ? Option<string>.None : effort);

        var refused = await starter.StartAsync(new AgentRequest("/work") { Connection = new ConnectionName(connection), Model = choice }, Cancellation);

        Assert.Equal(Enum.Parse<AgentError>(refusal), Outcomes.FailsWith(refused));
        Assert.Empty(launched);
    }

    [Fact]
    public async Task DeclaringAModelWritesItIntoTheConnectionsSettingsAndKeepsTheOthersAsync()
    {
        var registry = Registry("""{ "model": "small", "userHooks": true }""");

        Outcomes.Succeeds(await registry.DeclareAsync(
            new ConnectionName("work"),
            new ConnectionEdit(new ConnectionName("work"), "choosing") { Model = new ModelChoice("large", Option<string>.None) },
            Cancellation));

        Assert.Equal(Offered with { DefaultModel = "large" }, Outcomes.Present(Outcomes.Succeeds(await registry.CheckAsync(new ConnectionName("work"), Cancellation)).Capabilities.Get<OffersModels>()));
    }

    [Fact]
    public async Task DeclaringAModelTheHarnessDoesNotOfferIsRefusedBeforeTheFileIsTouchedAsync()
    {
        var registry = Registry("{}");

        var refused = await registry.DeclareAsync(
            new ConnectionName("work"),
            new ConnectionEdit(new ConnectionName("work"), "choosing") { Model = new ModelChoice(Option<string>.None, "max") },
            Cancellation);

        Assert.Equal(ConnectionError.UnofferedEffort, Outcomes.FailsWith(refused));
        Assert.True(Outcomes.Present(Outcomes.Succeeds(await registry.CheckAsync(new ConnectionName("work"), Cancellation)).Capabilities.Get<OffersModels>()).DefaultChoice().IsDefault);
    }

    [Fact]
    public async Task TheFormAsksTheHarnessWhatItOffersOnTheConnectionBeingEditedAsync()
    {
        var registry = Registry("""{ "model": "small" }""");

        var editing = Outcomes.Succeeds(await registry.CapabilitiesAsync(new ConnectionName("work"), new ConnectionEdit(new ConnectionName("work"), "choosing"), Cancellation));
        var adding = Outcomes.Succeeds(await registry.CapabilitiesAsync(Option<ConnectionName>.None, new ConnectionEdit(new ConnectionName("team"), "plain"), Cancellation));
        var unknown = await registry.CapabilitiesAsync(Option<ConnectionName>.None, new ConnectionEdit(new ConnectionName("team"), "nowhere"), Cancellation);

        Assert.Equal(Option<string>.Some("small"), Outcomes.Present(editing.Get<OffersModels>()).DefaultModel);
        Assert.False(adding.Has<OffersModels>());
        Assert.Equal(ConnectionError.UnknownProvider, Outcomes.FailsWith(unknown));
    }

    [Fact]
    public void TheFileEditSetsAndRemovesTheModelSettingsAndLeavesTheOthers()
    {
        const string file = """{ "connections": [ { "name": "work", "provider": "choosing", "settings": { "model": "small", "effort": "low", "userHooks": true } } ] }""";

        var chosen = Outcomes.Succeeds(ConnectionFileEdits.Apply(file, new DeclarationChange(new ConnectionName("work"), new ConnectionName("work"), "choosing", Option<CredentialDeclaration>.None)
        {
            Model = new ModelChoice("large", Option<string>.None),
        }));

        var settings = JsonNode.Parse(chosen)!["connections"]![0]!["settings"]!.AsObject();
        Assert.Equal(["model:large", "userHooks:true"], settings.Select(setting => $"{setting.Key}:{setting.Value}").Order(StringComparer.Ordinal));
    }

    private ConnectionRegistry Registry(string settings) =>
        Connected.Registry(
            $$"""{ "connections": [ { "name": "work", "provider": "choosing", "settings": {{settings}} }, { "name": "other", "provider": "plain" } ] }""",
            [choosing, plain]);
}
