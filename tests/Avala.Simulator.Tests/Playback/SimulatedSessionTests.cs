using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
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
