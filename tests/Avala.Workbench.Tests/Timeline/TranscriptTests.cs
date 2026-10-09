using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Canvas.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Timeline;

public sealed class TranscriptTests
{
    private static readonly SessionId Session = SessionId.New();
    private static readonly TurnId Turn = TurnId.New();
    private static readonly DateTimeOffset Start = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StreamedTextIsAppendedInOrderAndTheMessageClosesWithItsOutcome()
    {
        var transcript = Played(
            new ItemStarted(Session, Turn, new ItemId("reply"), ItemKind.Message, "Reply"),
            new ItemProgressed(Session, Turn, new ItemId("reply"), "Hello "),
            new ItemProgressed(Session, Turn, new ItemId("reply"), "team."),
            new ItemCompleted(Session, Turn, new ItemId("reply"), ItemOutcome.Succeeded));

        var message = Assert.IsType<MessageEntry>(Assert.Single(transcript.Entries));
        Assert.Equal(("Hello team.", Option<ItemOutcome>.Some(ItemOutcome.Succeeded)), (message.Text, message.Outcome));
    }

    [Fact]
    public void ReasoningMeasuresHowLongTheAgentThought()
    {
        var transcript = Transcript.Empty
            .Apply(new TurnStarted(Session, Turn), Start)
            .Apply(new ItemStarted(Session, Turn, new ItemId("think"), ItemKind.Reasoning, "Thinking"), Start)
            .Apply(new ItemProgressed(Session, Turn, new ItemId("think"), "The tests fail."), Start.AddSeconds(1))
            .Apply(new ItemCompleted(Session, Turn, new ItemId("think"), ItemOutcome.Succeeded), Start.AddSeconds(3));

        var reasoning = Assert.IsType<ReasoningEntry>(Assert.Single(transcript.Entries));
        Assert.Equal(("The tests fail.", Option<TimeSpan>.Some(TimeSpan.FromSeconds(3))), (reasoning.Text, reasoning.Duration));
    }

    [Fact]
    public void ToolItemsKeepTheirKindTitleOutputAndOutcome()
    {
        var transcript = Played(
            new ItemStarted(Session, Turn, new ItemId("edit"), ItemKind.FileEdit, "GREETING.md"),
            new ItemCompleted(Session, Turn, new ItemId("edit"), ItemOutcome.Succeeded),
            new ItemStarted(Session, Turn, new ItemId("test"), ItemKind.Command, "dotnet test"),
            new ItemProgressed(Session, Turn, new ItemId("test"), "Passed: 12"),
            new ItemCompleted(Session, Turn, new ItemId("test"), ItemOutcome.Failed));

        Assert.Equal(
            [(ItemKind.FileEdit, "GREETING.md", string.Empty, ItemOutcome.Succeeded), (ItemKind.Command, "dotnet test", "Passed: 12", ItemOutcome.Failed)],
            transcript.Entries.Cast<ToolEntry>().Select(tool => (tool.Kind, tool.Title, tool.Output, tool.Outcome.Match(outcome => outcome, () => ItemOutcome.Expired))));
    }

    [Fact]
    public void AnExecutedToolCallShowsItsInputAndTheResultItGot()
    {
        var transcript = Played(
            new ToolCalled(Session, Turn, new ItemId("call"), "delegate", """{ "instruction": "Write the notes" }"""),
            new ToolReturned(Session, Turn, new ItemId("call"), new ToolResult(new ItemId("call"), "integrated")),
            new ItemCompleted(Session, Turn, new ItemId("call"), ItemOutcome.Succeeded));

        var tool = Assert.IsType<ToolEntry>(Assert.Single(transcript.Entries));
        Assert.Equal(("delegate", """{ "instruction": "Write the notes" }""", "integrated"), (tool.Title, tool.Input.Match(input => input, () => string.Empty), tool.Output));
    }

    [Fact]
    public void APlanUpdateReplacesTheTurnsPlanInPlace()
    {
        var transcript = Played(
            new PlanUpdated(Session, Turn, [new PlanStep("Write", PlanStepStatus.InProgress), new PlanStep("Test", PlanStepStatus.Pending)]),
            new ItemStarted(Session, Turn, new ItemId("edit"), ItemKind.FileEdit, "GREETING.md"),
            new PlanUpdated(Session, Turn, [new PlanStep("Write", PlanStepStatus.Done), new PlanStep("Test", PlanStepStatus.InProgress)]));

        var plan = Assert.IsType<PlanEntry>(transcript.Entries[0]);
        Assert.Equal((2, 1, 2), (transcript.Entries.Count, plan.Done, plan.Total));
        Assert.Same(plan, transcript.Plan.Match(latest => latest, () => throw new InvalidOperationException()));
    }

    [Fact]
    public void ACanvasTakesItsContentFromTheThrottledSnapshotsOnly()
    {
        var canvas = new CanvasId(Turn, new ItemId("diagram"));
        var transcript = Played(
                new CanvasStarted(Session, Turn, canvas.Item, "Flow", "text/vnd.mermaid"),
                new ItemProgressed(Session, Turn, canvas.Item, "graph TD"))
            .Apply(new CanvasSnapshot(canvas, Session, "Flow", "text/vnd.mermaid", "graph TD; A-->B", CanvasStatus.Completed, false));

        var entry = Assert.IsType<CanvasEntry>(Assert.Single(transcript.Entries));
        Assert.Equal(("graph TD; A-->B", CanvasStatus.Completed, false), (entry.Content, entry.Status, entry.IsOffered));
    }

    [Fact]
    public void ACanvasClosesOnlyWithTheFinalSnapshotThatCarriesItsWholeContentWhetherItsItemEndsBeforeOrAfter()
    {
        var canvas = new CanvasId(Turn, new ItemId("diagram"));
        var started = Played(new CanvasStarted(Session, Turn, canvas.Item, "Flow", "text/vnd.mermaid"))
            .Apply(new CanvasSnapshot(canvas, Session, "Flow", "text/vnd.mermaid", "graph TD", CanvasStatus.Streaming, false));
        var final = new CanvasSnapshot(canvas, Session, "Flow", "text/vnd.mermaid", "graph TD; A-->B", CanvasStatus.Failed, false);

        var endedFirst = started.Apply(new ItemCompleted(Session, Turn, canvas.Item, ItemOutcome.Succeeded), Start);
        var closedFirst = started.Apply(final).Apply(new ItemCompleted(Session, Turn, canvas.Item, ItemOutcome.Succeeded), Start);

        Assert.Equal(
            [("graph TD", CanvasStatus.Streaming), ("graph TD; A-->B", CanvasStatus.Failed), ("graph TD; A-->B", CanvasStatus.Failed)],
            new[] { endedFirst, endedFirst.Apply(final), closedFirst }
                .Select(transcript => Assert.IsType<CanvasEntry>(Assert.Single(transcript.Entries)))
                .Select(entry => (entry.Content, entry.Status)));
    }

    [Fact]
    public void APermissionAwaitsAHumanOnlyOnceThePolicyLeftItToOneAndUntilItIsResolved()
    {
        var asked = Played(new PermissionRequested(Session, Turn, new ItemId("migrate"), "Run a command", ItemKind.Command, "dotnet ef database update"));
        var left = asked.Apply(Decision(DecisionDelivery.LeftToHuman));
        var resolved = left.Apply(new PermissionResolved(Session, Turn, new ItemId("migrate"), PermissionAnswer.Allow), Start);

        Assert.Equal([false, true, false], new[] { asked, left, resolved }.Select(transcript => transcript.Awaiting.Any()));
    }

    [Fact]
    public void APermissionThePolicyAnsweredNeverWentToAHuman()
    {
        var transcript = Played(new PermissionRequested(Session, Turn, new ItemId("edit"), "Edit a file", ItemKind.FileEdit, "GREETING.md"))
            .Apply(Decision(DecisionDelivery.Answered));

        var permission = Assert.IsType<PermissionEntry>(Assert.Single(transcript.Entries));
        Assert.Equal((false, false), (permission.WentToHuman, permission.AwaitsHuman));
    }

    [Fact]
    public void AFormLeftToAHumanAwaitsItsAnswerAndAFormThePolicyAnsweredDoesNot()
    {
        var asked = Played(new FormRequested(Session, Turn, new ItemId("question"), Question));
        var left = asked.Apply(FormDecision(Option<FormAnswer>.None));
        var answered = left.Apply(new FormAnswered(Session, Turn, new ItemId("question"), Answer), Start);
        var automatic = asked.Apply(FormDecision(Answer));

        Assert.Equal([false, true, false, false], new[] { asked, left, answered, automatic }.Select(transcript => transcript.Awaiting.Any()));
    }

    [Fact]
    public void TheEndOfATurnSumsItsUsageAndClosesTheCardsStillWaiting()
    {
        var transcript = Transcript.Empty
            .Apply(new TurnStarted(Session, Turn), Start)
            .Apply(new PermissionRequested(Session, Turn, new ItemId("migrate"), "Run a command", ItemKind.Command, "dotnet ef database update"), Start)
            .Apply(Decision(DecisionDelivery.LeftToHuman))
            .Apply(new UsageReported(Session, Turn, new TokenUsage(1_000, 50, 0, 0, 0), new Cost(0.01m, "USD")), Start)
            .Apply(new UsageReported(Session, Turn, new TokenUsage(200, 10, 0, 0, 0), new Cost(0.002m, "USD")), Start)
            .Apply(new TurnCompleted(Session, Turn, TurnOutcome.Interrupted), Start.AddSeconds(12));

        var ended = Assert.IsType<TurnEndEntry>(transcript.Entries[^1]);
        Assert.Equal(
            (TurnOutcome.Interrupted, TimeSpan.FromSeconds(12), 1_200L, 60L, 0.012m),
            (ended.Outcome, ended.Duration, ended.Tokens.Input, ended.Tokens.Output, Assert.Single(ended.Costs).Amount));
        Assert.Empty(transcript.Awaiting);
    }

    [Fact]
    public void AttemptsBecomePromptsUpdatedInPlaceWithTheirOutcome()
    {
        var submitted = Transcript.Empty.WithPrompts("Fix the failing test", []);
        var retried = submitted.WithPrompts(
            "Fix the failing test",
            [Attempt(1, AttemptOrigin.Initial, AttemptOutcome.Rejected), Attempt(2, AttemptOrigin.Retry, AttemptOutcome.Running, "tests failed")]);

        Assert.Equal(
            [(1, "Fix the failing test", "Rejected"), (2, "tests failed", "Running")],
            retried.Entries.Cast<PromptEntry>().Select(prompt => (prompt.Attempt, prompt.Text.Match(text => text, () => string.Empty), prompt.Outcome.Match(outcome => outcome.ToString(), () => string.Empty))));
        Assert.Equal(AttemptOrigin.Initial, Assert.IsType<PromptEntry>(Assert.Single(submitted.Entries)).Origin);
    }

    [Fact]
    public void ARestoredJobMarksWhereItsUnrecordedPastEnds()
    {
        var restored = Transcript.Empty.WithPrompts("Fix the failing test", [Attempt(1, AttemptOrigin.Initial, AttemptOutcome.Interrupted)]).WithRestart();
        var recovered = restored.WithPrompts(
            "Fix the failing test",
            [Attempt(1, AttemptOrigin.Initial, AttemptOutcome.Interrupted), Attempt(2, AttemptOrigin.Recovery, AttemptOutcome.Running)]);

        Assert.Equal([typeof(PromptEntry), typeof(RestartEntry), typeof(PromptEntry)], recovered.Entries.Select(entry => entry.GetType()));
        Assert.Empty(Transcript.Empty.WithRestart().Entries);
    }

    [Fact]
    public void ContentForAnItemThatNeverStartedIsIgnored()
    {
        var transcript = Played(
            new ItemProgressed(Session, Turn, new ItemId("ghost"), "text"),
            new ItemCompleted(Session, Turn, new ItemId("ghost"), ItemOutcome.Succeeded));

        Assert.Empty(transcript.Entries);
    }

    private static AgentForm Question { get; } = new(
        FormPurpose.Question,
        "Choose a database",
        "The service stores orders.",
        [new FormField("database", "Database", "Which one?", FieldKind.SingleChoice, [new FormOption("PostgreSQL", "Relational", Recommended: true)])]);

    private static FormAnswer Answer { get; } = new(new ItemId("question"), [new FieldAnswer("database") { Chosen = ["PostgreSQL"] }]);

    private static Transcript Played(params IAgentEvent[] activity) =>
        activity.Aggregate(Transcript.Empty.Apply(new TurnStarted(Session, Turn), Start), (transcript, next) => transcript.Apply(next, Start));

    private static PolicyDecision Decision(DecisionDelivery delivery) =>
        new(Session, Turn, new ItemId(delivery == DecisionDelivery.Answered ? "edit" : "migrate"), Option<JobId>.None, ItemKind.Command, "target", delivery == DecisionDelivery.Answered ? PolicyAnswer.Allow : PolicyAnswer.Ask, Option<PolicyRule>.None, delivery, Start);

    private static FormDecision FormDecision(Option<FormAnswer> answer) =>
        new(Session, Turn, new ItemId("question"), Option<JobId>.None, Question, Autonomy.Supervised, answer, [], answer.IsSome ? DecisionDelivery.Answered : DecisionDelivery.LeftToHuman, Start);

    private static AttemptRecord Attempt(int number, AttemptOrigin origin, AttemptOutcome outcome, string guidance = "") =>
        new(number, origin, outcome, string.IsNullOrEmpty(guidance) ? Option<string>.None : guidance, Option<SessionId>.None);
}
