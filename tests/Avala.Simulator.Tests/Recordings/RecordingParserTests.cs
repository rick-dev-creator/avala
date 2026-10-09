using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Simulator.Recordings;
using Avala.Simulator.Scenarios;
using Avala.Testing;

namespace Avala.Simulator.Tests.Recordings;

public sealed class RecordingParserTests
{
    [Theory]
    [InlineData("""{ "format": "avala-recording", "version": 2, "options": { "permissions": "askEveryTime" }, "entries": [] }""", nameof(ReplayError.UnsupportedVersion))]
    [InlineData("""{ "format": "something-else", "version": 1, "options": { "permissions": "askEveryTime" }, "entries": [] }""", nameof(ReplayError.Malformed))]
    [InlineData("""{ "format": "avala-recording", "version": 1, "options": { "permissions": "1" }, "entries": [] }""", nameof(ReplayError.Malformed))]
    [InlineData("""{ "format": "avala-recording", "version": 1, "options": { "permissions": "askEveryTime" }, "entries": [ { "at": 0, "event": { "type": "teleported", "turn": 1 } } ] }""", nameof(ReplayError.Malformed))]
    [InlineData("not json", nameof(ReplayError.Malformed))]
    public void OnlyARecordingOfAVersionTheSimulatorKnowsIsReplayed(string text, string expected) =>
        Assert.Equal(Enum.Parse<ReplayError>(expected), Outcomes.FailsWith(RecordingParser.Parse(text, "/work")));

    [Fact]
    public void AnInputTheProviderRefusedIsNotExpectedAgainAndUnknownEntriesAreIgnored()
    {
        var recorded = Outcomes.Succeeds(RecordingParser.Parse(
            Recorded.Session(
                Recorded.TurnStarted,
                """{ "at": 12, "respond": { "item": "edit", "answer": "allow" }, "refused": "noPendingPermission" }""",
                """{ "at": 13, "note": { "text": "added by a later version" } }""",
                """{ "at": 14, "stop": {} }"""),
            "/work"));

        Assert.Equal(PermissionMode.AskEveryTime, recorded.Permissions);
        Assert.Equal([typeof(Emit)], recorded.Steps.Select(step => step.GetType()));
    }

    [Fact]
    public void ACanvasAndAUsageLimitThatResetsAreReplayedWithTheirFields()
    {
        var recorded = Outcomes.Succeeds(RecordingParser.Parse(
            Recorded.Session(
                """{ "at": 0, "event": { "type": "canvasStarted", "turn": 1, "item": "sketch", "title": "Sketch of ${workingDirectory}", "mediaType": "image/svg+xml" } }""",
                """{ "at": 5, "event": { "type": "limitReported", "turn": 1, "limit": { "window": "fiveHours", "usedFraction": 0.5, "resetsAt": "2026-10-09T13:30:00+00:00" } } }"""),
            "/work"));

        Assert.Equal(
            [
                new CanvasStarted(default, default, new ItemId("sketch"), "Sketch of /work", "image/svg+xml"),
                new LimitReported(default, default, new UsageLimit("fiveHours", 0.5, new DateTimeOffset(2026, 10, 9, 13, 30, 0, TimeSpan.Zero))),
            ],
            recorded.Steps.Cast<Emit>().Select(emit => emit.Event));
    }
}
