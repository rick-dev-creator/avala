using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Agents.Sessions;
using Avala.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avala.Agents.Tests.Sessions;

public sealed class AgentSessionsTests
{
    private static readonly AgentRequest Request = new("/worktrees/1");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASessionOpensInTheRequestedFolderAndForwardsEveryEventInOrderAsync()
    {
        var provider = new ScriptedAgentProvider(ScriptedAgentProvider.Reply);
        var bus = new RecordingBus();
        await using var agents = Agents(bus, provider);

        var turn = await StartAsync(agents, "Add GitHub login");
        await bus.WaitForAsync<TurnFinished>(_ => true, Cancellation);

        var session = Assert.Single(provider.Sessions);
        Assert.Equal("/worktrees/1", session.Options.WorkingDirectory);
        Assert.Equal(["Add GitHub login"], session.Received);
        Assert.Equal(
            ScriptedAgentProvider.Reply(turn.Session, turn.Turn),
            bus.Published.OfType<AgentActivity>().Select(activity => activity.Event));
    }

    [Fact]
    public async Task AnnouncesTheEndOfATurnWithItsOutcomeAsync()
    {
        var bus = new RecordingBus();
        await using var agents = Agents(bus, new ScriptedAgentProvider(ScriptedAgentProvider.Reply));

        var turn = await StartAsync(agents, "Add GitHub login");

        Assert.Equal(
            new TurnFinished(turn.Session, turn.Turn, TurnOutcome.Finished),
            await bus.WaitForAsync<TurnFinished>(_ => true, Cancellation));
    }

    [Fact]
    public async Task ClosesItemsLeftOpenBeforeTheTurnFinishesAsync()
    {
        var bus = new RecordingBus();
        await using var agents = Agents(bus, new ScriptedAgentProvider((session, turn) =>
        [
            new TurnStarted(session, turn),
            new ItemStarted(session, turn, new ItemId("build"), ItemKind.Command, "Build"),
            new TurnCompleted(session, turn, TurnOutcome.Finished),
        ]));

        var turn = await StartAsync(agents, "Add GitHub login");
        await bus.WaitForAsync<TurnFinished>(_ => true, Cancellation);

        Assert.Equal(
            [
                new ItemCompleted(turn.Session, turn.Turn, new ItemId("build"), ItemOutcome.Abandoned),
                new TurnCompleted(turn.Session, turn.Turn, TurnOutcome.Finished),
            ],
            bus.Published.OfType<AgentActivity>().Select(activity => activity.Event).Skip(2));
    }

    [Fact]
    public async Task CannotOpenASessionWithoutAProviderAsync()
    {
        await using var agents = Agents(new RecordingBus());

        Assert.Equal(AgentError.ProviderUnavailable, Outcomes.FailsWith(await agents.OpenAsync(Request, Cancellation)));
    }

    [Fact]
    public async Task EveryMessageStartsANewTurnInTheSameSessionAsync()
    {
        var provider = new ScriptedAgentProvider(ScriptedAgentProvider.Reply);
        await using var agents = Agents(new RecordingBus(), provider);
        var first = await StartAsync(agents, "Add GitHub login");

        var second = Outcomes.Succeeds(await agents.SendAsync(first.Session, "Two tests fail", Cancellation));

        Assert.Equal(first.Session, second.Session);
        Assert.NotEqual(first.Turn, second.Turn);
        Assert.Equal(["Add GitHub login", "Two tests fail"], Assert.Single(provider.Sessions).Received);
    }

    [Fact]
    public async Task AStoppedSessionTakesNoMoreMessagesAsync()
    {
        var provider = new ScriptedAgentProvider(ScriptedAgentProvider.Reply);
        await using var agents = Agents(new RecordingBus(), provider);
        var turn = await StartAsync(agents, "Add GitHub login");

        Outcomes.Succeeds(await agents.StopAsync(turn.Session, Cancellation));

        Assert.True(Assert.Single(provider.Sessions).IsDisposed);
        Assert.Equal(AgentError.SessionClosed, Outcomes.FailsWith(await agents.SendAsync(turn.Session, "late", Cancellation)));
    }

    [Fact]
    public async Task APermissionDecisionReachesTheSessionThatAskedForItAsync()
    {
        var provider = new ScriptedAgentProvider(ScriptedAgentProvider.Reply);
        await using var agents = Agents(new RecordingBus(), provider);
        var turn = await StartAsync(agents, "Add GitHub login");
        var decision = new PermissionDecision(new ItemId("migrate"), PermissionAnswer.Allow);

        var answered = Outcomes.Succeeds(await agents.RespondAsync(turn.Session, decision, Cancellation));

        Assert.Equal(new ItemId("migrate"), answered);
        Assert.Equal([decision], Assert.Single(provider.Sessions).Decisions);
    }

    [Fact]
    public async Task CannotAnswerAPermissionInASessionThatIsNotOpenAsync()
    {
        await using var agents = Agents(new RecordingBus(), new ScriptedAgentProvider(ScriptedAgentProvider.Reply));
        var decision = new PermissionDecision(new ItemId("migrate"), PermissionAnswer.Allow);

        Assert.Equal(
            AgentError.SessionClosed,
            Outcomes.FailsWith(await agents.RespondAsync(SessionId.New(), decision, Cancellation)));
    }

    private static async Task<AgentTurn> StartAsync(AgentSessions agents, string instruction)
    {
        var session = Outcomes.Succeeds(await agents.OpenAsync(Request, Cancellation));

        return Outcomes.Succeeds(await agents.SendAsync(session, instruction, Cancellation));
    }

    private static AgentSessions Agents(RecordingBus bus, params IAgentProvider[] providers) =>
        new(providers, bus, TimeProvider.System, NullLogger<AgentSessions>.Instance);
}
