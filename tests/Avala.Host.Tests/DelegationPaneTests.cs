using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class DelegationPaneTests(PublishedPlugins plugins)
{
    private static readonly (string File, string Content)[] Login =
    [
        ("connections.json", """{ "default": "work", "connections": [ { "name": "work", "provider": "simulator", "credential": { "source": "login" } } ] }"""),
        ("connections/work/.login", string.Empty),
    ];

    [Fact]
    public async Task TheDelegationPaneHangsEachSubagentAndChildUnderTheJobThatStartedItWithTheOrchestratorMarkedAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            Login,
            [
                (".avala/permissions.json", """{ "autonomy": "autonomous" }"""),
                (".avala/jobs.json", """{ "delegation": { "connections": ["work"], "maxChildren": 1 } }"""),
            ]);
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-agents] Ship the release")));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(job);
        var header = workbench.Page["Regions"]["Header"].Items;
        var toggle = header.Single(item => item.Kind == "DelegationToggleViewModel");
        var pane = workbench.Page["Regions"]["Preview"].Items.Single(item => item.Kind == "DelegationPaneViewModel");
        await run.Ui.PresentedAsync(toggle.Presentation, () => toggle["IsShown"].Value<bool>(), () => "toggle hidden");

        await run.Ui.InvokeAsync(() => toggle.Execute("ToggleCommand"), TestContext.Current.CancellationToken);
        await run.Ui.PresentedAsync(pane.Presentation, () => Branches(pane).Count() == 4 && Settled(pane), () => "pane not settled");

        var shown = await run.Ui.ReadAsync(() => (
            pane["IsOpen"].Value<bool>(),
            workbench.Page["Regions"]["IsPreviewOpen"].Value<bool>(),
            Branches(pane).Select(branch => (branch.Kind, branch["Place"]["Depth"].Value<int>(), branch["Place"]["StateText"].Text)).ToList(),
            Branches(pane).Select(branch => branch.Has("IsViewing") && branch["IsViewing"].Value<bool>()).ToList(),
            Branches(pane).Where(branch => branch.Kind == "SubagentViewModel").Select(agent => agent["Name"].Text).ToList()));
        Assert.True(shown.Item1);
        Assert.True(shown.Item2);
        Assert.Equal(
            [("OrchestratorCardViewModel", 0, "completed"), ("SubagentViewModel", 1, "completed"), ("DelegationNodeViewModel", 1, "completed"), ("SubagentViewModel", 2, "completed")],
            shown.Item3);
        Assert.Equal([true, false, false, false], shown.Item4);
        Assert.Equal(["Plan the split", "Read the changelog"], shown.Item5);
        Assert.True(await run.Ui.ReadAsync(() => toggle["IsOpen"].Value<bool>()));
    }

    [Fact]
    public async Task ASubagentStartedWhileItsJobIsShownOpensThePaneWithItsCommandAndUsageWhileItsToolsStayOutOfTheConversationAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "subagents");
        var install = await run.DecisionAsync();
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        var pane = workbench.Page["Regions"]["Preview"].Items.Single(item => item.Kind == "DelegationPaneViewModel");
        var closed = await run.Ui.ReadAsync(() => pane["IsOpen"].Value<bool>());

        Outcomes.Succeeds(await run.Get<IAgents>().RespondAsync(install.Session, new PermissionDecision(install.Item, PermissionAnswer.Allow), TestContext.Current.CancellationToken));
        var build = await run.DecisionAsync();
        await workbench.ShowsAsync(pane, () => AtTheBuild(pane) && AsksForTheBuild(conversation));

        var agent = await run.Ui.ReadAsync(() => Branches(pane).Single(branch => branch.Kind == "SubagentViewModel"));
        var shown = await run.Ui.ReadAsync(() => (agent["Name"].Text, agent["Activity"].Text, agent["Meter"].Text));
        var rows = await run.Ui.ReadAsync(() => conversation["Entries"].Items.Where(entry => entry.Kind == "ToolViewModel").Select(tool => tool["Title"].Text).ToList());
        Assert.False(closed);
        Assert.Equal("survey-build", build.Item.Value);
        Assert.Equal(("Survey the greeting", "Run dotnet build", "3 tool uses · 3,600 tokens"), shown);
        Assert.DoesNotContain(rows, title => title.Contains("greeting", StringComparison.Ordinal) && !title.StartsWith("Subagent", StringComparison.Ordinal));
        Assert.DoesNotContain(rows, title => title.Contains("dotnet build", StringComparison.Ordinal));
    }

    private static bool Settled(Bound pane) =>
        Branches(pane).First()["Place"]["StateText"].Text == "completed";

    private static bool AtTheBuild(Bound pane) =>
        pane["IsOpen"].Value<bool>() && Branches(pane).Where(branch => branch.Kind == "SubagentViewModel").Select(agent => agent["Activity"].Text).SequenceEqual(["Run dotnet build"]);

    private static bool AsksForTheBuild(Bound conversation) =>
        conversation["Entries"].Items.Any(entry => entry.Kind == "PermissionCardViewModel" && entry["Title"].Text == "Run dotnet build");

    private static IEnumerable<Bound> Branches(Bound pane) =>
        pane["HasTree"].Value<bool>() ? pane["Tree"]["Branches"].Items : [];
}
