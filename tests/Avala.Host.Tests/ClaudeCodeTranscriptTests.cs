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
}
