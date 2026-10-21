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
    public void ADraftACanvasAUsageLimitThatResetsAndAWithdrawnRequestAreReplayedWithTheirFields()
    {
        var work = HostPaths.Rooted("/work");
        var recorded = Outcomes.Succeeds(RecordingParser.Parse(
            Recorded.Session(
                """{ "at": 0, "event": { "type": "itemDrafted", "turn": 1, "item": "write", "kind": "fileEdit", "title": "Write ${workingDirectory}/notes.md", "received": 512 } }""",
                """{ "at": 0, "event": { "type": "canvasStarted", "turn": 1, "item": "sketch", "title": "Sketch of ${workingDirectory}", "mediaType": "image/svg+xml" } }""",
                """{ "at": 5, "event": { "type": "limitReported", "turn": 1, "limit": { "window": "fiveHours", "usedFraction": 0.5, "resetsAt": "2026-10-09T13:30:00+00:00" } } }""",
                """{ "at": 6, "event": { "type": "requestWithdrawn", "turn": 1, "item": "migrate" } }"""),
            work));

        Assert.Equal(
            [
                new ItemDrafted(default, default, new ItemId("write"), ItemKind.FileEdit, $"Write {Path.Combine(work, "notes.md")}", 512),
                new CanvasStarted(default, default, new ItemId("sketch"), $"Sketch of {work}", "image/svg+xml"),
                new LimitReported(default, default, new UsageLimit("fiveHours", 0.5, new DateTimeOffset(2026, 10, 9, 13, 30, 0, TimeSpan.Zero))),
                new RequestWithdrawn(default, default, new ItemId("migrate")),
            ],
            recorded.Steps.Cast<Emit>().Select(emit => emit.Event));
    }

    [Fact]
    public void AHarnessRetryingIsReplayedWithItsAttemptWaitAndReason()
    {
        var recorded = Outcomes.Succeeds(RecordingParser.Parse(
            Recorded.Session("""{ "at": 5, "event": { "type": "harnessRetrying", "turn": 1, "attempt": 3, "most": 10, "waitMs": 4000, "reason": "overloaded" } }"""),
            "/work"));

        Assert.Equal(new HarnessRetrying(default, default, 3, 10, TimeSpan.FromSeconds(4), "overloaded"), recorded.Steps.Cast<Emit>().Single().Event);
    }

    [Fact]
    public void AContextReportKeepsWhereItsConnectionCompactsAndOneWithoutItHasNone()
    {
        var recorded = Outcomes.Succeeds(RecordingParser.Parse(
            Recorded.Session(
                """{ "at": 5, "event": { "type": "contextReported", "turn": 1, "used": 59306, "window": 1000000, "compacted": false, "compactsAt": 250000 } }""",
                """{ "at": 6, "event": { "type": "contextReported", "turn": 1, "used": 61000, "window": 1000000, "compacted": true } }"""),
            "/work"));

        Assert.Equal(
            [Avala.Sdk.Option<long>.Some(250_000), Avala.Sdk.Option<long>.None],
            recorded.Steps.Cast<Emit>().Select(emit => ((ContextReported)emit.Event).CompactsAt));
    }
}
