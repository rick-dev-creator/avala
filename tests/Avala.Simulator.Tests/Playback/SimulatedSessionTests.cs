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
        await using var stage = new Stage();
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
        await using var stage = new Stage();
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
            await stage.ReadTurnAsync(Cancellation));
    }

    [Fact]
    public async Task TheCanvasScenarioStreamsAnSvgInSeveralChunksAsync()
    {
        await using var stage = new Stage();
        await stage.SendAsync("[simulate: canvas] Draw the architecture", Cancellation);

        var events = await stage.ReadTurnAsync(Cancellation);
        var svg = Assert.Single(events.OfType<CanvasStarted>(), canvas => canvas.MediaType == "image/svg+xml");
        var chunks = events.OfType<ItemProgressed>().Where(progressed => progressed.Item == svg.Item).Select(progressed => progressed.Text).ToList();

        Assert.True(chunks.Count > 1);
        Assert.StartsWith("<svg", string.Concat(chunks), StringComparison.Ordinal);
        Assert.EndsWith("</svg>", string.Concat(chunks), StringComparison.Ordinal);
    }
}
