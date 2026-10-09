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
}
