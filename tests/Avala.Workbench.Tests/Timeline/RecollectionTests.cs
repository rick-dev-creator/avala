using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Canvas.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Transcripts.Contracts;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Timeline;

public sealed class RecollectionTests
{
    private const string Instruction = "Fix the failing test";

    private static readonly SessionId Session = SessionId.New();
    private static readonly TurnId Turn = TurnId.New();
    private static readonly DateTimeOffset Start = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid FirstRun = Guid.CreateVersion7();

    [Fact]
    public void AJobOfAnEarlierRunShowsWhatWasKeptInOrderThenAMarkThatSaysItWasKept()
    {
        var recalled = Recollection.Recalled(
            Instruction,
            [Attempt(1, AttemptOrigin.Initial, AttemptOutcome.Passed)],
            [
                Kept(new AttemptBegan(1)),
                Kept(new AgentActed(new TurnStarted(Session, Turn))),
                Kept(new AgentActed(new ItemStarted(Session, Turn, new ItemId("run"), ItemKind.Command, "dotnet test") { Input = "dotnet test" })),
                Kept(new AgentActed(new ItemProgressed(Session, Turn, new ItemId("run"), "Passed!"))),
                Kept(new AgentActed(new ItemCompleted(Session, Turn, new ItemId("run"), ItemOutcome.Succeeded))),
                Kept(new AgentActed(new ItemStarted(Session, Turn, new ItemId("reply"), ItemKind.Message, "Reply"))),
                Kept(new AgentActed(new ItemProgressed(Session, Turn, new ItemId("reply"), "Done."))),
                Kept(new AgentActed(new ItemCompleted(Session, Turn, new ItemId("reply"), ItemOutcome.Succeeded))),
                Kept(new AgentActed(new TurnCompleted(Session, Turn, TurnOutcome.Finished)), seconds: 5),
            ]);

        Assert.Equal(
            ["prompt 1 Fix the failing test Passed", "tool dotnet test Passed! Succeeded", "message Done. Succeeded", "turn Finished 00:00:05", "restart kept"],
            recalled.Entries.Select(Describe));
    }

    [Fact]
    public void WhatWasLeftOpenWhenTheApplicationStoppedIsClosedSoNothingWaitsOnADeadSession()
    {
        var recalled = Recollection.Recalled(
            Instruction,
            [Attempt(1, AttemptOrigin.Initial, AttemptOutcome.Interrupted)],
            [
                Kept(new AttemptBegan(1)),
                Kept(new AgentActed(new TurnStarted(Session, Turn))),
                Kept(new AgentActed(new ItemStarted(Session, Turn, new ItemId("think"), ItemKind.Reasoning, "Thinking"))),
                Kept(new AgentActed(new PermissionRequested(Session, Turn, new ItemId("migrate"), "Run a command", ItemKind.Command, "dotnet ef database update"))),
                Kept(new PermissionRuled(new PolicyDecision(Session, Turn, new ItemId("migrate"), Option<JobId>.None, ItemKind.Command, "dotnet ef database update", PolicyAnswer.Ask, Option<PolicyRule>.None, DecisionDelivery.LeftToHuman, Start))),
                Kept(new AgentActed(new CanvasStarted(Session, Turn, new ItemId("canvas"), "Flow", "image/svg+xml"))),
                Kept(new CanvasDrawn(new CanvasSnapshot(new CanvasId(Turn, new ItemId("canvas")), Session, "Flow", "image/svg+xml", "<svg/>", CanvasStatus.Streaming, true)), seconds: 2),
            ]);

        Assert.Empty(recalled.Awaiting);
        Assert.Equal(
            ["prompt 1 Fix the failing test Interrupted", "reasoning Abandoned 00:00:02", "permission closed", "canvas Abandoned", "restart kept"],
            recalled.Entries.Select(Describe));
    }

    [Fact]
    public void EachRestartBetweenEarlierRunsIsMarkedWhereItHappenedAndLaterAttemptsFollowTheLastMark()
    {
        var secondRun = Guid.CreateVersion7();

        var recalled = Recollection.Recalled(
            Instruction,
            [Attempt(1, AttemptOrigin.Initial, AttemptOutcome.Interrupted), Attempt(2, AttemptOrigin.Recovery, AttemptOutcome.Interrupted), Attempt(3, AttemptOrigin.Recovery, AttemptOutcome.Running)],
            [
                Kept(new AttemptBegan(1)),
                Kept(new AgentActed(new ItemStarted(Session, Turn, new ItemId("reply"), ItemKind.Message, "Reply"))),
                Kept(new AttemptBegan(2)) with { Run = secondRun },
            ]);

        Assert.Equal(
            ["prompt 1 Fix the failing test Interrupted", "message  Abandoned", "restart kept", "prompt 2  Interrupted", "restart kept", "prompt 3  Running"],
            recalled.Entries.Select(Describe));
    }

    [Fact]
    public void AJobThatRanBeforeConversationsWereKeptShowsItsAttemptsThenAMarkThatSaysSo()
    {
        var recalled = Recollection.Recalled(Instruction, [Attempt(1, AttemptOrigin.Initial, AttemptOutcome.Passed)], []);

        Assert.Equal(["prompt 1 Fix the failing test Passed", "restart not kept"], recalled.Entries.Select(Describe));
    }

    private static KeptFact Kept(ITranscriptFact fact, int seconds = 0) => new(FirstRun, Start.AddSeconds(seconds), fact);

    private static AttemptRecord Attempt(int number, AttemptOrigin origin, AttemptOutcome outcome) =>
        new(number, origin, outcome, Option<string>.None, Option<SessionId>.None);

    private static string Describe(ITimelineEntry entry) => entry switch
    {
        PromptEntry prompt => $"prompt {prompt.Attempt} {prompt.Text.Match(text => text, () => string.Empty)} {prompt.Outcome.Match(outcome => outcome.ToString(), () => string.Empty)}",
        ToolEntry tool => $"tool {tool.Input.Match(input => input, () => string.Empty)} {tool.Output} {tool.Outcome.Match(outcome => outcome.ToString(), () => string.Empty)}",
        MessageEntry message => $"message {message.Text} {message.Outcome.Match(outcome => outcome.ToString(), () => string.Empty)}",
        ReasoningEntry reasoning => $"reasoning {reasoning.Outcome.Match(outcome => outcome.ToString(), () => string.Empty)} {reasoning.Duration.Match(duration => duration.ToString(), () => string.Empty)}",
        PermissionEntry permission => permission.Closed ? "permission closed" : "permission open",
        CanvasEntry canvas => $"canvas {canvas.Status}",
        TurnEndEntry ended => $"turn {ended.Outcome} {ended.Duration}",
        RestartEntry restart => restart.Kept ? "restart kept" : "restart not kept",
        _ => entry.GetType().Name,
    };
}
