using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Simulator.Scenarios;
using Avala.Testing;

namespace Avala.Simulator.Tests.Playback;

public sealed class SimulatedSessionTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheEditScenarioWritesItsFileIntoTheWorkingDirectoryAsync()
    {
        await using var stage = new Stage(PermissionMode.AllowAll);
        await stage.SendAsync("[simulate: edit] Add a greeting", Cancellation);

        var events = await stage.ReadTurnAsync(Cancellation);

        Assert.Contains(events, agentEvent => agentEvent is ItemStarted { Kind: ItemKind.FileEdit, Title: "Edit GREETING.md" });
        Assert.Equal(
            "# Hello\n\nWritten by the simulator.\n",
            await File.ReadAllTextAsync(Path.Combine(stage.WorkingDirectory, "GREETING.md"), Cancellation));
    }

    [Fact]
    public async Task TheProcessesScenarioBuildsThenLeavesAServerListeningOnTheLeasedPortPastItsSessionAsync()
    {
        var port = Testing.Workloads.FreePort().ToString(System.Globalization.CultureInfo.InvariantCulture);
        await using var trees = new RecordingProcessTrees(new Dictionary<string, string> { ["AVALA_PORT"] = port });
        var tree = Assert.IsType<RecordingTree>(await trees.OpenAsync(".", Cancellation));
        var stage = new Stage(tree, PermissionMode.AllowAll);
        await stage.SendAsync("[simulate: processes] Try the service", Cancellation);

        var output = (await stage.ReadTurnAsync(Cancellation)).OfType<ItemProgressed>()
            .GroupBy(progressed => progressed.Item.Value)
            .ToDictionary(item => item.Key, item => string.Concat(item.Select(progressed => progressed.Text)));
        await stage.CloseSessionAsync();
        var build = await Testing.Workloads.IsGoneAsync(tree.Started[0]);
        var server = await Testing.Workloads.IsGoneAsync(tree.Started[1]);
        await trees.DisposeAsync();
        await stage.DisposeAsync();

        Assert.StartsWith("done", output["build"], StringComparison.Ordinal);
        Assert.Equal($"listening {port}", output["serve"]);
        Assert.Equal((true, false), (build, server));
    }

    [Fact]
    public async Task FixAfterFeedbackWritesABrokenFileFirstAndFixesItAfterFeedbackAsync()
    {
        await using var stage = new Stage(PermissionMode.AllowAll);
        var calculator = Path.Combine(stage.WorkingDirectory, "calculator.txt");

        await stage.SendAsync("[simulate: fix-after-feedback] Add a calculator", Cancellation);
        await stage.ReadTurnAsync(Cancellation);
        var first = await File.ReadAllTextAsync(calculator, Cancellation);
        await stage.SendAsync("add(2, 2) should be 4", Cancellation);
        await stage.ReadTurnAsync(Cancellation);

        Assert.Contains("BROKEN", first, StringComparison.Ordinal);
        Assert.Equal("add(2, 2) = 4\n", await File.ReadAllTextAsync(calculator, Cancellation));
    }

    [Fact]
    public async Task RewriteChecksEmptiesTheCheckDeclarationWithABrokenFileAndFixesTheFileAfterFeedbackAsync()
    {
        await using var stage = new Stage(PermissionMode.AllowAll);
        var calculator = Path.Combine(stage.WorkingDirectory, "calculator.txt");

        await stage.SendAsync("[simulate: rewrite-checks] Add a calculator", Cancellation);
        await stage.ReadTurnAsync(Cancellation);
        var first = await File.ReadAllTextAsync(calculator, Cancellation);
        await stage.SendAsync("add(2, 2) should be 4", Cancellation);
        await stage.ReadTurnAsync(Cancellation);

        Assert.Equal("{ \"checks\": [] }\n", await File.ReadAllTextAsync(Path.Combine(stage.WorkingDirectory, ".avala", "checks.json"), Cancellation));
        Assert.Contains("BROKEN", first, StringComparison.Ordinal);
        Assert.Equal("add(2, 2) = 4\n", await File.ReadAllTextAsync(calculator, Cancellation));
    }

    [Fact]
    public async Task AskingEveryTimeAnEditAsksPermissionForItsFileBeforeWritingItAsync()
    {
        await using var stage = new Stage(PermissionMode.AskEveryTime);
        var greeting = Path.Combine(stage.WorkingDirectory, "GREETING.md");
        await stage.SendAsync("[simulate: edit] Add a greeting", Cancellation);

        var requested = Assert.IsType<PermissionRequested>((await stage.ReadUntilAsync<PermissionRequested>(Cancellation))[^1]);

        Assert.Equal((new ItemId("edit"), ItemKind.FileEdit, greeting), (requested.Item, requested.Kind, requested.Target));
        Assert.False(File.Exists(greeting));
        Outcomes.Succeeds(await stage.Session.RespondAsync(new PermissionDecision(requested.Item, PermissionAnswer.Allow), Cancellation));
        await stage.ReadUntilAsync<ItemCompleted>(Cancellation);
        Assert.True(File.Exists(greeting));
    }

    [Fact]
    public async Task AskingEveryTimeADeniedEditIsCancelledWithoutWritingAndTheTurnFinishesAsync()
    {
        await using var stage = new Stage(PermissionMode.AskEveryTime);
        var turn = await stage.SendAsync("[simulate: edit] Add a greeting", Cancellation);
        var requested = Assert.IsType<PermissionRequested>((await stage.ReadUntilAsync<PermissionRequested>(Cancellation))[^1]);

        Outcomes.Succeeds(await stage.Session.RespondAsync(new PermissionDecision(requested.Item, PermissionAnswer.Deny), Cancellation));

        Assert.Equal(
            [
                new PermissionResolved(stage.Session.Id, turn, requested.Item, PermissionAnswer.Deny),
                new ItemCompleted(stage.Session.Id, turn, requested.Item, ItemOutcome.Cancelled),
                new TurnCompleted(stage.Session.Id, turn, TurnOutcome.Finished),
            ],
            await stage.ReadTurnAsync(Cancellation));
        Assert.Empty(Directory.EnumerateFileSystemEntries(stage.WorkingDirectory));
    }

    [Theory]
    [InlineData(PermissionMode.AskEveryTime, "edit", "edit test")]
    [InlineData(PermissionMode.AllowEdits, "edit", "")]
    [InlineData(PermissionMode.AllowAll, "edit", "")]
    [InlineData(PermissionMode.AskEveryTime, "permission", "migrate")]
    [InlineData(PermissionMode.AllowEdits, "permission", "migrate")]
    [InlineData(PermissionMode.AllowAll, "permission", "")]
    public async Task EachPermissionModeAsksForTheActionsItDoesNotAllowAsync(PermissionMode mode, string scenario, string asked)
    {
        await using var stage = new Stage(mode);
        await stage.SendAsync($"[simulate: {scenario}] Work", Cancellation);

        var turn = await stage.ReadTurnAllowingEveryRequestAsync(Cancellation);

        Assert.Equal(asked, string.Join(' ', turn.OfType<PermissionRequested>().Select(requested => requested.Item.Value)));
        Assert.Equal(TurnOutcome.Finished, Assert.IsType<TurnCompleted>(turn[^1]).Outcome);
    }

    [Fact]
    public async Task OnlyTheFirstMessageChoosesTheScenarioAsync()
    {
        await using var stage = new Stage();
        await stage.SendAsync("Hello", Cancellation);
        await stage.ReadTurnAsync(Cancellation);

        await stage.SendAsync("[simulate: edit] Add a greeting", Cancellation);
        var second = await stage.ReadTurnAsync(Cancellation);

        Assert.DoesNotContain(second, agentEvent => agentEvent is ItemStarted { Kind: ItemKind.FileEdit });
        Assert.Empty(Directory.EnumerateFileSystemEntries(stage.WorkingDirectory));
    }

    [Fact]
    public async Task APermissionRequestHoldsTheTurnUntilTheCommandIsAllowedAsync()
    {
        await using var stage = new Stage();
        var turn = await stage.SendAsync("[simulate: permission] Migrate the database", Cancellation);
        var requested = Assert.IsType<PermissionRequested>((await stage.ReadUntilAsync<PermissionRequested>(Cancellation))[^1]);

        var rest = stage.ReadTurnAsync(Cancellation);
        Assert.False(rest.IsCompleted);
        var answered = Outcomes.Succeeds(await stage.Session.RespondAsync(new PermissionDecision(requested.Item, PermissionAnswer.Allow), Cancellation));

        Assert.Equal(requested.Item, answered);
        Assert.Equal(
            [
                new PermissionResolved(stage.Session.Id, turn, requested.Item, PermissionAnswer.Allow),
                new ItemProgressed(stage.Session.Id, turn, requested.Item, "Applied 2 migrations."),
                new ItemCompleted(stage.Session.Id, turn, requested.Item, ItemOutcome.Succeeded),
            ],
            (await rest).Take(3));
        Assert.Equal(new TurnCompleted(stage.Session.Id, turn, TurnOutcome.Finished), (await rest)[^1]);
    }

    [Fact]
    public async Task ADeniedCommandIsCancelledAndTheTurnFinishesWithoutRunningItAsync()
    {
        await using var stage = new Stage();
        var turn = await stage.SendAsync("[simulate: permission] Migrate the database", Cancellation);
        var requested = Assert.IsType<PermissionRequested>((await stage.ReadUntilAsync<PermissionRequested>(Cancellation))[^1]);

        Outcomes.Succeeds(await stage.Session.RespondAsync(new PermissionDecision(requested.Item, PermissionAnswer.Deny), Cancellation));

        Assert.Equal(
            [
                new PermissionResolved(stage.Session.Id, turn, requested.Item, PermissionAnswer.Deny),
                new ItemCompleted(stage.Session.Id, turn, requested.Item, ItemOutcome.Cancelled),
                new TurnCompleted(stage.Session.Id, turn, TurnOutcome.Finished),
            ],
            await stage.ReadTurnAsync(Cancellation));
    }

    [Fact]
    public async Task ADenialWithAMessageReachesTheAgentWhichRepliesWithItBeforeTheTurnFinishesAsync()
    {
        await using var stage = new Stage();
        var turn = await stage.SendAsync("[simulate: permission] Migrate the database", Cancellation);
        var requested = Assert.IsType<PermissionRequested>((await stage.ReadUntilAsync<PermissionRequested>(Cancellation))[^1]);
        var denial = new PermissionDecision(requested.Item, PermissionAnswer.Deny) { Message = "Use the staging database." };

        Outcomes.Succeeds(await stage.Session.RespondAsync(denial, Cancellation));

        var reply = new ItemId("migrate-reply");
        Assert.Equal(
            [
                new PermissionResolved(stage.Session.Id, turn, requested.Item, PermissionAnswer.Deny),
                new ItemCompleted(stage.Session.Id, turn, requested.Item, ItemOutcome.Cancelled),
                new ItemStarted(stage.Session.Id, turn, reply, ItemKind.Message, "Reply"),
                new ItemProgressed(stage.Session.Id, turn, reply, "Understood, I will not go on: Use the staging database."),
                new ItemCompleted(stage.Session.Id, turn, reply, ItemOutcome.Succeeded),
                new TurnCompleted(stage.Session.Id, turn, TurnOutcome.Finished),
            ],
            await stage.ReadTurnAsync(Cancellation));
    }

    [Fact]
    public async Task TheQuestionScenarioWaitsForAnAnswerAndGoesOnWithTheChoiceAsync()
    {
        await using var stage = new Stage();
        var turn = await stage.SendAsync("[simulate: question] Store the orders", Cancellation);
        var asked = Assert.IsType<FormRequested>((await stage.ReadUntilAsync<FormRequested>(Cancellation))[^1]);
        var field = Assert.Single(asked.Form.Fields);
        var answer = new FormAnswer(asked.Item, [new FieldAnswer(field.Id) { Chosen = ["SQLite"] }]);

        Outcomes.Succeeds(await stage.Session.AnswerAsync(answer, Cancellation));

        var rest = await stage.ReadTurnAsync(Cancellation);
        Assert.Equal((FormPurpose.Question, "PostgreSQL"), (asked.Form.Purpose, Assert.Single(field.Options, option => option.Recommended).Label));
        Assert.Equal(
            [
                new FormAnswered(stage.Session.Id, turn, asked.Item, answer),
                new ItemCompleted(stage.Session.Id, turn, asked.Item, ItemOutcome.Succeeded),
            ],
            rest.Take(2));
        Assert.Contains(rest, cue => cue is ItemProgressed { Text: "Going with Database: SQLite" });
        Assert.Equal(new TurnCompleted(stage.Session.Id, turn, TurnOutcome.Finished), rest[^1]);
    }

    [Fact]
    public async Task APlanThatIsNotApprovedIsNotCarriedOutAsync()
    {
        await using var stage = new Stage();
        var turn = await stage.SendAsync("[simulate: plan-approval] Add an endpoint", Cancellation);
        var asked = Assert.IsType<FormRequested>((await stage.ReadUntilAsync<FormRequested>(Cancellation))[^1]);

        Outcomes.Succeeds(await stage.Session.AnswerAsync(
            new FormAnswer(asked.Item, [new FieldAnswer("approve") { Confirmed = false, Text = "Split it in two." }]),
            Cancellation));

        var rest = await stage.ReadTurnAsync(Cancellation);
        Assert.Equal(FormPurpose.PlanApproval, asked.Form.Purpose);
        Assert.Contains(rest, cue => cue is ItemProgressed { Text: "Going with Plan: not approved (Split it in two.)" });
        Assert.Equal(new TurnCompleted(stage.Session.Id, turn, TurnOutcome.Finished), rest[^1]);
        Assert.DoesNotContain(rest, cue => cue is PermissionRequested);
        Assert.Empty(Directory.EnumerateFileSystemEntries(stage.WorkingDirectory));
    }

    [Fact]
    public async Task AnsweringAnItemThatNoFormWaitsOnFailsAsync()
    {
        await using var stage = new Stage();
        await stage.SendAsync("[simulate: question] Store the orders", Cancellation);
        await stage.ReadUntilAsync<FormRequested>(Cancellation);

        var answer = new FormAnswer(new ItemId("deploy"), [new FieldAnswer("database") { Chosen = ["SQLite"] }]);

        Assert.Equal(AgentError.NoPendingForm, Outcomes.FailsWith(await stage.Session.AnswerAsync(answer, Cancellation)));
    }

    [Fact]
    public async Task AnsweringAnItemThatIsNotWaitingForPermissionFailsAsync()
    {
        await using var stage = new Stage();
        await stage.SendAsync("[simulate: permission] Migrate the database", Cancellation);

        var decision = new PermissionDecision(new ItemId("deploy"), PermissionAnswer.Allow);

        Assert.Equal(AgentError.NoPendingPermission, Outcomes.FailsWith(await stage.Session.RespondAsync(decision, Cancellation)));
    }

    [Fact]
    public async Task TheCrashScenarioBreaksTheEventStreamMidTurnAsync()
    {
        await using var stage = new Stage();
        await stage.SendAsync("[simulate: crash] Start", Cancellation);
        var seen = new List<IAgentEvent>();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var agentEvent in stage.Session.Events.WithCancellation(Cancellation))
            {
                seen.Add(agentEvent);
            }
        });

        Assert.IsType<ItemStarted>(seen[^1]);
        Assert.DoesNotContain(seen, agentEvent => agentEvent is TurnCompleted);
    }

    [Fact]
    public async Task InterruptingAHangingTurnEndsItAsInterruptedAsync()
    {
        await using var stage = new Stage();
        var turn = await stage.SendAsync("[simulate: hang] Wait", Cancellation);

        Assert.Equal(turn, Outcomes.Succeeds(await stage.Session.InterruptAsync(Cancellation)));
        Assert.Equal(
            [new TurnStarted(stage.Session.Id, turn), new TurnCompleted(stage.Session.Id, turn, TurnOutcome.Interrupted)],
            (await stage.ReadTurnAsync(Cancellation)).Where(agentEvent => agentEvent is not ResumeTokenIssued));
    }

    [Fact]
    public async Task AnInterruptedTurnHasEndedWhenTheInterruptionReturnsSoTheNextTurnStartsAtOnceAsync()
    {
        var refusals = new List<AgentError>();

        for (var round = 0; round < 200; round++)
        {
            await using var stage = new Stage();
            await stage.SendAsync("[simulate: hang] Wait", Cancellation);
            Outcomes.Succeeds(await stage.Session.InterruptAsync(Cancellation));

            if (!(await stage.Session.SendAsync(new UserTurn("Carry on where you stopped."), Cancellation)).TryGetValue(out _, out var refusal))
            {
                refusals.Add(refusal);
            }
        }

        Assert.Empty(refusals);
    }

    [Fact]
    public async Task GivenTheCanvasToolTheCanvasScenarioStreamsAnSvgThroughItInSeveralChunksAsync()
    {
        await using var stage = new Stage(PermissionMode.AskEveryTime, Stage.CanvasTool);
        await stage.SendAsync("[simulate: canvas] Draw the architecture", Cancellation);

        var events = await stage.ReadTurnAsync(Cancellation);
        var svg = Assert.Single(events.OfType<CanvasStarted>(), canvas => canvas.MediaType == "image/svg+xml");
        var chunks = events.OfType<ItemProgressed>().Where(progressed => progressed.Item == svg.Item).Select(progressed => progressed.Text).ToList();

        Assert.True(chunks.Count > 1);
        Assert.StartsWith("<svg", string.Concat(chunks), StringComparison.Ordinal);
        Assert.EndsWith("</svg>", string.Concat(chunks), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutTheCanvasToolTheCanvasScenarioWritesItsDrawingsAsMessagesAsync()
    {
        await using var stage = new Stage();
        await stage.SendAsync("[simulate: canvas] Draw the architecture", Cancellation);

        var events = await stage.ReadTurnAsync(Cancellation);

        Assert.DoesNotContain(events, agentEvent => agentEvent is CanvasStarted);
        Assert.Contains(events, agentEvent => agentEvent is ItemStarted { Item.Value: "diagram", Kind: ItemKind.Message });
    }

    [Fact]
    public async Task GivenTheHarnessToolTheFollowUpScenarioWaitsForTheResultOfItsCallAndRepliesWithItAsync()
    {
        await using var stage = new Stage(PermissionMode.AllowAll, new HarnessTool(ScenarioCatalog.ProposeFollowUp, "Propose", "{}", ToolSurface.Executed));
        await stage.SendAsync("[simulate: follow-up] Start a changelog", Cancellation);
        var called = Assert.IsType<ToolCalled>((await stage.ReadUntilAsync<ToolCalled>(Cancellation))[^1]);
        var result = new ToolResult(called.Item, "Accepted as a follow-up.");

        Assert.Equal(AgentError.NoPendingCall, Outcomes.FailsWith(await stage.Session.ReturnAsync(result with { Item = new ItemId("other") }, Cancellation)));
        Assert.Equal(called.Item, Outcomes.Succeeds(await stage.Session.ReturnAsync(result, Cancellation)));

        var events = await stage.ReadTurnAsync(Cancellation);
        Assert.Equal(ScenarioCatalog.ProposeFollowUp, called.Tool);
        Assert.Contains("[simulate: reply]", called.Input, StringComparison.Ordinal);
        Assert.Equal(
            [new ToolReturned(stage.Session.Id, called.Turn, called.Item, result), new ItemCompleted(stage.Session.Id, called.Turn, called.Item, ItemOutcome.Succeeded)],
            events.Take(2));
        Assert.Contains(events, agentEvent => agentEvent is ItemProgressed { Text: "The harness answered: Accepted as a follow-up." });
    }

    [Fact]
    public async Task TheDelegateScenarioCallsTwoToolsAtOnceAndReportsEachResultAsItArrivesAsync()
    {
        await using var stage = new Stage(PermissionMode.AllowAll, new HarnessTool(ScenarioCatalog.Delegate, "Delegate", "{}", ToolSurface.Executed));
        await stage.SendAsync("[simulate: delegate] Write the notes and the to-do list", Cancellation);
        var first = Assert.IsType<ToolCalled>((await stage.ReadUntilAsync<ToolCalled>(Cancellation))[^1]);
        var second = Assert.IsType<ToolCalled>(Assert.Single(await stage.ReadUntilAsync<ToolCalled>(Cancellation)));

        Assert.Equal(second.Item, Outcomes.Succeeds(await stage.Session.ReturnAsync(new ToolResult(second.Item, "To-do written."), Cancellation)));
        var answered = await stage.ReadUntilAsync<ItemCompleted>(Cancellation);
        Assert.Equal(first.Item, Outcomes.Succeeds(await stage.Session.ReturnAsync(new ToolResult(first.Item, "Notes written."), Cancellation)));
        var events = await stage.ReadTurnAsync(Cancellation);

        Assert.Equal(
            [(ScenarioCatalog.Delegate, "[simulate: notes]"), (ScenarioCatalog.Delegate, "[simulate: todo]")],
            new[] { first, second }.Select(called => (called.Tool, called.Input[(called.Input.IndexOf('[', StringComparison.Ordinal))..(called.Input.IndexOf(']', StringComparison.Ordinal) + 1)])));
        Assert.Equal(
            [new ToolReturned(stage.Session.Id, second.Turn, second.Item, new ToolResult(second.Item, "To-do written.")), new ItemCompleted(stage.Session.Id, second.Turn, second.Item, ItemOutcome.Succeeded)],
            answered);
        Assert.Equal(new ToolReturned(stage.Session.Id, first.Turn, first.Item, new ToolResult(first.Item, "Notes written.")), events[0]);
        Assert.Contains(events, agentEvent => agentEvent is ItemProgressed { Text: "The harness answered: To-do written. Notes written." });
        Assert.IsType<TurnCompleted>(events[^1]);
    }

    [Fact]
    public async Task WithoutTheHarnessToolTheFollowUpScenarioWritesItsProposalAsAMessageAsync()
    {
        await using var stage = new Stage(PermissionMode.AllowAll);
        await stage.SendAsync("[simulate: follow-up] Start a changelog", Cancellation);

        var events = await stage.ReadTurnAsync(Cancellation);

        Assert.DoesNotContain(events, agentEvent => agentEvent is ToolCalled);
        Assert.Contains(events, agentEvent => agentEvent is ItemProgressed progressed && progressed.Text.StartsWith("I would call propose_follow_up", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheNearLimitScenarioReportsALimitThatResetsTwoSecondsAfterItsReportAsync()
    {
        await using var stage = new Stage();
        var before = TimeProvider.System.GetUtcNow();
        await stage.SendAsync("[simulate: near-limit] Use the window", Cancellation);

        var limit = Assert.Single((await stage.ReadTurnAsync(Cancellation)).OfType<LimitReported>()).Limit;

        var resets = Outcomes.Present(limit.ResetsAt);
        Assert.Equal(("5h", 0.95), (limit.Window, limit.UsedFraction));
        Assert.InRange(resets, before.AddSeconds(2), TimeProvider.System.GetUtcNow().AddSeconds(2));
    }

    [Fact]
    public async Task EveryTurnIssuesAResumeTokenThatContinuesTheConversationWithItsNextTurnAsync()
    {
        await using var stage = new Stage(PermissionMode.AllowAll);
        await stage.SendAsync("[simulate: fix-after-feedback] Add a calculator", Cancellation);
        var first = await stage.ReadTurnAsync(Cancellation);
        var issued = Assert.IsType<ResumeTokenIssued>(first[1]);

        await using var resumed = Outcomes.Succeeds(await stage.ResumeAsync(issued.Token, Cancellation));
        Outcomes.Succeeds(await resumed.SendAsync(new UserTurn("Continue"), Cancellation));
        var next = await Stage.ReadUntilAsync<TurnCompleted>(resumed, Cancellation);

        Assert.Contains(next, agentEvent => agentEvent is ItemStarted { Item.Value: "fix" });
        Assert.Equal("add(2, 2) = 4\n", await File.ReadAllTextAsync(Path.Combine(stage.WorkingDirectory, "calculator.txt"), Cancellation));
        Assert.NotEqual(issued.Token, Assert.IsType<ResumeTokenIssued>(next[1]).Token);
    }

    [Theory]
    [InlineData("not a token")]
    [InlineData("0123456789abcdef0123456789abcdef/unknown/1")]
    public async Task AResumeTokenTheSimulatorNeverIssuedIsRejectedAsync(string token)
    {
        await using var stage = new Stage();

        Assert.Equal(AgentError.CannotResume, Outcomes.FailsWith(await stage.ResumeAsync(new ResumeToken(token), Cancellation)));
    }
}
