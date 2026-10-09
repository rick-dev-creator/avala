using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Simulator.Scenarios;

namespace Avala.Simulator.Tests.Scenarios;

public sealed class CuesTests
{
    private static readonly ItemId Item = new("edit");

    private static readonly ResumeToken Token = new("live-token");

    private readonly Cues recording = new(SessionId.New(), TurnId.New());

    private readonly Cues live = new(SessionId.New(), TurnId.New());

    [Fact]
    public void ARecordedEventIsReplayedInTheLiveSessionAndTurnWithEverythingElseKept()
    {
        IAgentEvent[] recorded =
        [
            recording.Opened(Item, ItemKind.FileEdit, "Edit GREETING.md"),
            new CanvasStarted(recording.Session, recording.Turn, Item, "Sketch", "image/svg+xml"),
            recording.Progressed(Item, "# Hello"),
            recording.Asked(Item, "Edit GREETING.md", ItemKind.FileEdit, "/work/GREETING.md"),
            recording.Answered(Item, PermissionAnswer.Allow),
            recording.Called(Item, "write_todo", "{}"),
            recording.Returned(Item, new ToolResult(Item, "To-do written.")),
            recording.Closed(Item, ItemOutcome.Succeeded),
            new PlanUpdated(recording.Session, recording.Turn, [new PlanStep("Greet", PlanStepStatus.InProgress)]),
            new LimitReported(recording.Session, recording.Turn, new UsageLimit("fiveHours", 0.5, Option<DateTimeOffset>.None)),
            recording.Ended(TurnOutcome.Finished),
        ];

        var replayed = recorded.Select(cue => live.Readdress(cue, Token)).ToList();

        Assert.All(replayed, cue => Assert.Equal((live.Session, live.Turn), (cue.Session, cue.Turn)));
        Assert.Equal(recorded, replayed.Select(cue => recording.Readdress(cue, Token)));
    }

    [Fact]
    public void AnEventOfAKindTheSimulatorDoesNotKnowIsReplayedUnchanged()
    {
        var unknown = new Unknown(recording.Session, recording.Turn);

        Assert.Same(unknown, live.Readdress(unknown, Token));
    }

    private sealed record Unknown(SessionId Session, TurnId Turn) : IAgentEvent;
}
