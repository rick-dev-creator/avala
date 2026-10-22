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
            new ItemDrafted(recording.Session, recording.Turn, Item, ItemKind.FileEdit, "Edit GREETING.md", 512),
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

    [Fact]
    public void ASubagentIsOpenedWithItsPromptMeteredAndClosedWithItsOutput()
    {
        var agent = new ItemId("agent");

        IAgentEvent[] played = [.. new IStep[] { new StartSubagent(agent, "explorer", "Map the module"), new MeterSubagent(agent, 1_200), new EndSubagent(agent, "Mapped.") }.SelectMany(live.Of)];

        Assert.Equal(
            [
                live.Opened(agent, ItemKind.Subagent, "Subagent: explorer", "Map the module"),
                live.Metered(agent, 1_200),
                live.Progressed(agent, "Mapped."),
                live.Closed(agent, ItemOutcome.Succeeded),
            ],
            played);
    }

    [Fact]
    public void WhatThePersonToldIsRunAsACommandOrReturnedByATool()
    {
        var told = live with { Told = "dotnet test" };

        Assert.Equal(
            [
                told.Opened(Item, ItemKind.Command, "Run dotnet test", "dotnet test"),
                told.Closed(Item, ItemOutcome.Succeeded),
                told.Opened(Item, ItemKind.Mcp, "mcp__notes__read", string.Empty),
                told.Progressed(Item, "dotnet test"),
                told.Closed(Item, ItemOutcome.Succeeded),
            ],
            new IStep[] { new RunTold(Item), new ReturnTold(Item, "mcp__notes__read") }.SelectMany(told.Of));
    }

    [Fact]
    public void RecallingRepeatsWhatThePersonToldAndWhatTheyAttached()
    {
        var told = live with { Told = "the tax line", Attached = [new Attachment("screen.png", "image/png", "/home/ana/screen.png")] };

        Assert.Equal(
            ["I was told: the tax line", "I received screen.png (image/png).", "Nothing was attached."],
            new[] { told.Of(new Recall(Item)), told.Of(new RecallAttachments(Item)), live.Of(new RecallAttachments(Item)) }.Select(events => string.Concat(events.OfType<ItemProgressed>().Select(progressed => progressed.Text))));
    }

    [Fact]
    public void AMessageFromTheDeviceBeginsATurnOnlyWhenItIsWhatThePersonSentFromIt()
    {
        var told = live with { Told = "Also cover partial refunds" };

        Assert.Equal([new RemoteMessageArrived(live.Session, live.Turn, "Also cover partial refunds") { BeganTurn = true }], told.Of(new FromDevice("Also cover partial refunds")));
        Assert.Empty(told.Of(new FromDevice("Something else")));
    }

    [Fact]
    public void AStepOfAKindTheCuesDoNotKnowPlaysNothing() =>
        Assert.Empty(live.Of(new Silent()));

    private sealed record Silent : IStep;

    private sealed record Unknown(SessionId Session, TurnId Turn) : IAgentEvent;
}
