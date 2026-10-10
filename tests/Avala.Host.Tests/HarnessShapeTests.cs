using System.Globalization;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Supervision.Contracts;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class HarnessShapeTests(PublishedPlugins plugins)
{
    private const string WebAllowed = """{ "autonomy": "autonomous", "rules": [ { "name": "web", "kind": "web", "answer": "allow" } ] }""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryToolRowShowsTheInputItWasCalledWithAndASubagentRowItsOwnTextAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "tools", (".avala/permissions.json", WebAllowed));

        var rows = await ToolRowsAsync(run);

        Assert.Equal(
            [
                ("Search", "Search greeting", "greeting in src/**/*.cs", "src/Greeter.cs:3: // greeting goes here"),
                ("Web", "Fetch https://example.com/style", "https://example.com/style\nHow should a greeting be written?", "Greetings start with a heading."),
                ("Other", "Load the canvas tool", "canvas", "canvas"),
                ("Subagent", "Subagent: Find the greeting style", "Read the repository and say how greetings are written.", "Reading the repository.\n\nGreetings are a single heading line."),
                ("FileEdit", "Edit GREETING.md", "# Hello\n", "# Hello\n"),
                ("Command", "Run cat > NOTES.md <<'EOF' +3 lines", "cat > NOTES.md <<'EOF'\n# Notes\nWritten through the shell.\nEOF", string.Empty),
            ],
            rows);
        Assert.Equal("# Notes\nWritten through the shell.\n", await File.ReadAllTextAsync(Path.Combine(run.Worktree, "NOTES.md"), Cancellation));
    }

    [Fact]
    public async Task ClaudeCodesToolRowsShowTheirInputAndItsSubagentsTextStaysInTheSubagentsRowAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("tools");
        await using var run = await SimulatedRun.TranscribedAsync(
            plugins,
            claude.Plugin,
            new JobRequest(string.Empty, "Find how the project greets, check the style guide and the web, then write the findings to NOTES.md."),
            claude.Data,
            [(".avala/permissions.json", WebAllowed)]);

        var rows = await ToolRowsAsync(run);

        Assert.Equal(
            [
                ("Search", "Search greet", "greet in ."),
                ("Other", "Load mcp__avala__propose_follow_up", "select:mcp__avala__propose_follow_up"),
                ("Web", "Fetch https://example.com/style", "https://example.com/style\nHow should a greeting be written?"),
                ("Web", "Search the web for team greeting conventions", "team greeting conventions"),
                ("Subagent", "Subagent: Read the README", "Read README.md and say in one sentence how the project greets."),
                ("Other", "Read README.md", "README.md"),
                ("Command", "Run cat > NOTES.md <<'EOF' +3 lines", "cat > NOTES.md <<'EOF'\n# Notes\nGreetings are level one headings that name the team.\nEOF"),
            ],
            rows.Select(row => (row.Kind, row.Title, row.Input)));
        Assert.Equal(
            "I will read the README.\n\nThe project greets the team with a level one heading.",
            rows.Single(row => row.Kind == "Subagent").Output);
        Assert.Equal(
            ["Let me look around first.", "NOTES.md records that greetings are headings that name the team."],
            await run.Ui.ReadAsync(() => OpenWorkbench.Texts(run.Page("Jobs")["Conversation"], "MessageViewModel", "Text")));
    }

    [Fact]
    public async Task ADelegatedCallThatWaitsHoursForItsChildNeitherStallsNorExpiresAndIsAnsweredWhenTheChildFinishesAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            [("supervision.json", $$"""{ "silenceSeconds": {{TimeSpan.FromMinutes(10).TotalSeconds.ToString(CultureInfo.InvariantCulture)}} }""")],
            [(".avala/jobs.json", """{ "delegation": {} }""")]);
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("delegate-waiting"))));
        var asked = await run.DecisionAsync();

        run.Clock.Advance(TimeSpan.FromHours(2));
        Outcomes.Succeeds(await run.Get<IAgents>().RespondAsync(asked.Session, new PermissionDecision(asked.Item, PermissionAnswer.Allow), Cancellation));

        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(job)));
        Assert.Empty(run.Get<ISupervision>().OfJob(job));
    }

    private static async Task<List<(string Kind, string Title, string Input, string Output)>> ToolRowsAsync(SimulatedRun run)
    {
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await workbench.ShowsAsync(() => OpenWorkbench.Entry(conversation, "TurnEndViewModel") is not null);

        return await run.Ui.ReadAsync(() => conversation["Entries"].Items
            .Where(entry => entry.Kind == "ToolViewModel")
            .Select(tool => (tool["Kind"].Text, tool["Title"].Text, tool["Input"].Text, tool["Output"].Text))
            .ToList());
    }
}
