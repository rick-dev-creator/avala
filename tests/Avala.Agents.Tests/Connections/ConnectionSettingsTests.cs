using System.Text.Json.Nodes;
using Avala.Agents.ConnectionFiles;
using Avala.Agents.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Agents.Contracts.Connections;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Agents.Tests.Connections;

public sealed class ConnectionSettingsTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DeclaringSettingsSetsAndRemovesThemKeepsTheOthersAndTheCatalogShowsThemAsync()
    {
        var plain = new ScriptedAgentProvider(ScriptedAgentProvider.Reply) { Info = new ProviderInfo("plain", "Plain") };
        var registry = Connected.Registry("""{ "connections": [ { "name": "work", "provider": "plain", "settings": { "userHooks": "true", "autoCompact": "false" } } ] }""", [plain]);

        var catalog = Outcomes.Succeeds(await registry.DeclareAsync(
            new ConnectionName("work"),
            new ConnectionEdit(new ConnectionName("work"), "plain")
            {
                Settings = new Dictionary<string, Option<string>> { ["compactAt"] = "400000", ["autoCompact"] = Option<string>.None },
            },
            Cancellation));

        Assert.Equal(
            ["compactAt:400000", "userHooks:true"],
            catalog.Connections.Single(connection => connection.Name.Value == "work").Settings.Select(setting => $"{setting.Key}:{setting.Value}").Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheFileEditWritesSettingsAsStringsAndDropsAnEmptySettingsObject()
    {
        const string file = """{ "connections": [ { "name": "work", "provider": "plain", "settings": { "compactAt": "150000" } } ] }""";

        var cleared = Outcomes.Succeeds(ConnectionFileEdits.Apply(file, new DeclarationChange(new ConnectionName("work"), new ConnectionName("work"), "plain", Option<CredentialDeclaration>.None)
        {
            Settings = new Dictionary<string, Option<string>> { ["compactAt"] = Option<string>.None },
        }));
        var set = Outcomes.Succeeds(ConnectionFileEdits.Apply(cleared, new DeclarationChange(new ConnectionName("work"), new ConnectionName("work"), "plain", Option<CredentialDeclaration>.None)
        {
            Settings = new Dictionary<string, Option<string>> { ["idleCompaction"] = "false" },
        }));

        Assert.Null(JsonNode.Parse(cleared)!["connections"]![0]!["settings"]);
        Assert.Equal("false", JsonNode.Parse(set)!["connections"]![0]!["settings"]!["idleCompaction"]!.GetValue<string>());
    }
}
