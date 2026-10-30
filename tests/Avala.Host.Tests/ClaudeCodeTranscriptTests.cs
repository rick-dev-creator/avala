using System.Text.Json.Nodes;
using Avala.Jobs.Contracts;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class ClaudeCodeTranscriptTests(PublishedPlugins plugins)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> Jobs => new()
    {
        { "tasks", "Track the work with tasks: write GREETING.md, then polish it." },
        { "tools", "Find how the project greets, check the style guide and the web, then write the findings to NOTES.md." },
        { "plan-approval", "Plan the greeting first and ask me to approve the plan, then write GREETING.md." },
        { "canvas", "Draw the architecture on the canvas as an SVG diagram." },
    };

    [Theory]
    [MemberData(nameof(Jobs))]
    public async Task AClaudeCodeSessionRunsAsAJobToItsExpectedOutcomeAndRecordsTheCommittedRecordingAsync(string transcript, string instruction)
    {
        var name = $"claude-code-{transcript}";
        var fixture = await RegressionFixture.LoadAsync(name, Cancellation);
        using var claude = await TranscribedClaude.PrepareAsync(transcript);
        await using var run = await SimulatedRun.TranscribedAsync(plugins, claude.Plugin, new JobRequest(string.Empty, instruction), claude.Data, fixture.Committed);

        var outcome = await RecordedSessionTests.OutcomeAsync(run, fixture);
        var recording = Assert.Single(await run.StopAndReadRecordingsAsync());

        if (RecordingFixtures.Refreshing)
        {
            await File.WriteAllTextAsync(RecordingFixtures.RecordingOf(name), recording, Cancellation);
        }

        Assert.Equal(fixture.Expected, outcome);
        Assert.Equal(
            TranscribedClaude.Comparable(await File.ReadAllTextAsync(RecordingFixtures.RecordingOf(name), Cancellation)),
            TranscribedClaude.Comparable(recording));
    }

    [Fact]
    public async Task AClaudeCodeSessionIsAskedToShowVisualsOnTheCanvasWhichItLoadsWithoutToolSearchAsync()
    {
        var fixture = await RegressionFixture.LoadAsync("claude-code-canvas", Cancellation);
        using var claude = await TranscribedClaude.PrepareAsync("canvas");
        await using var tapped = new TemporaryFolder();
        await using var run = await SimulatedRun.TranscribedAsync(plugins, claude.Plugin, new JobRequest(string.Empty, "Draw the architecture on the canvas as an SVG diagram."), claude.Tapping(tapped.Path), fixture.Committed);

        Assert.Equal(fixture.Expected, await RecordedSessionTests.OutcomeAsync(run, fixture));
        _ = await run.StopAndReadRecordingsAsync();
        var lines = (await File.ReadAllLinesAsync(Assert.Single(Directory.GetFiles(tapped.Path, "*.jsonl")), Cancellation)).Select(line => JsonNode.Parse(line)!).ToList();
        var arguments = lines[0]["arguments"]!.AsArray().Select(argument => (string)argument!).ToList();
        var listed = lines.Select(line => line["in"]?["response"]?["response"]?["mcp_response"]?["result"]?["tools"]).OfType<JsonArray>().Single();

        Assert.Equal(
            "Avala shows the person your work inside its conversation. When you are asked to show something visual, such as a chart, a diagram or a drawing, "
            + "call the mcp__avala__canvas tool with SVG or Mermaid: Avala draws it right there "
            + "and plays its SVG animations written in SMIL, such as <animate> or <animateTransform>, so a chart may grow or a flow may move. "
            + "Do not write an HTML file or open a browser to show it, unless it must respond to input, such as an interactive chart, a three.js scene or an app: "
            + "then make it a page, as follows. "
            + "For a page or an app, keep every file in the worktree, scratch files included, and vendor its libraries into it instead of loading them from a CDN. "
            + "Serve it bound to 127.0.0.1 on port $AVALA_PORT, or start its dev server there; the ports $AVALA_PORTS are yours. "
            + "Then tell the person its URL, such as http://localhost:$AVALA_PORT: Avala shows it beside the conversation. "
            + "Never open a browser or take screenshots. "
            + "A 3D model needs no page: write it into the worktree as .glb, .gltf, .stl or .obj and give its path, and Avala shows it in its own viewer.",
            arguments[arguments.IndexOf("--append-system-prompt") + 1]);
        Assert.True((bool)listed.Single(tool => (string)tool!["name"]! == "canvas")!["_meta"]!["anthropic/alwaysLoad"]!);
    }
}
