using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Agents.Sessions;
using Avala.Sdk;
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
    public async Task ASessionAsksPermissionForEveryActionSoThatEveryActionMeetsThePolicyAsync()
    {
        var provider = new ScriptedAgentProvider(ScriptedAgentProvider.Reply);
        await using var agents = Agents(new RecordingBus(), provider);

        Outcomes.Succeeds(await agents.OpenAsync(Request, Cancellation));

        Assert.Equal(PermissionMode.AskEveryTime, Assert.Single(provider.Sessions).Options.Permissions);
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
    public async Task OpeningASessionAnnouncesItWithItsProviderAccountAndWorkingDirectoryBeforeAnyActivityAsync()
    {
        var account = new AgentAccount("acct-1", "Team account");
        var provider = new ScriptedAgentProvider(ScriptedAgentProvider.Reply) { Account = () => account };
        var bus = new RecordingBus();
        await using var agents = Agents(bus, provider);

        var turn = await StartAsync(agents, "Add GitHub login");
        await bus.WaitForAsync<TurnFinished>(_ => true, Cancellation);

        Assert.Equal(new SessionOpened(turn.Session, provider.Info, Request.WorkingDirectory) { Account = account }, bus.Published[0]);
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
        Assert.True(agents.IsOpen(turn.Session));

        Outcomes.Succeeds(await agents.StopAsync(turn.Session, Cancellation));

        Assert.False(agents.IsOpen(turn.Session));
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

    [Fact]
    public async Task InterruptingReachesTheAgentOfASessionWhoseProviderCanInterruptAsync()
    {
        var provider = new ScriptedAgentProvider((session, turn) => [new TurnStarted(session, turn)], canInterrupt: true);
        await using var agents = Agents(new RecordingBus(), provider);
        var turn = await StartAsync(agents, "Add GitHub login");

        Assert.Equal(turn.Turn, Outcomes.Succeeds(await agents.InterruptAsync(turn.Session, Cancellation)));
        Assert.Equal(1, Assert.Single(provider.Sessions).Interruptions);
    }

    [Fact]
    public async Task InterruptingIsUnsupportedWhenTheProviderCannotInterruptAsync()
    {
        var provider = new ScriptedAgentProvider((session, turn) => [new TurnStarted(session, turn)]);
        await using var agents = Agents(new RecordingBus(), provider);
        var turn = await StartAsync(agents, "Add GitHub login");

        Assert.Equal(AgentError.Unsupported, Outcomes.FailsWith(await agents.InterruptAsync(turn.Session, Cancellation)));
        Assert.Equal(0, Assert.Single(provider.Sessions).Interruptions);
    }

    [Fact]
    public async Task CannotInterruptASessionThatIsNotOpenAsync()
    {
        await using var agents = Agents(new RecordingBus(), new ScriptedAgentProvider(ScriptedAgentProvider.Reply, canInterrupt: true));

        Assert.Equal(AgentError.SessionClosed, Outcomes.FailsWith(await agents.InterruptAsync(SessionId.New(), Cancellation)));
    }

    [Fact]
    public async Task ASessionWhoseStreamFailsIsReportedCrashedBeforeItsLiveTurnFailsAsync()
    {
        var bus = new RecordingBus();
        var provider = new ScriptedAgentProvider((session, turn) => [new TurnStarted(session, turn)]);
        await using var agents = Agents(bus, provider);
        var turn = await StartAsync(agents, "Add GitHub login");

        Assert.Single(provider.Sessions).Crash();

        Assert.Equal(TurnOutcome.Failed, (await bus.WaitForAsync<TurnFinished>(_ => true, Cancellation)).Outcome);
        Assert.Equal(
            [new SessionEnded(turn.Session, SessionEnding.Crashed), new TurnFinished(turn.Session, turn.Turn, TurnOutcome.Failed)],
            bus.Published.Where(published => published is SessionEnded or TurnFinished));
    }

    [Fact]
    public async Task ASessionWhoseStreamEndsMidTurnIsReportedClosedAndItsTurnFailsAsync()
    {
        var bus = new RecordingBus();
        var provider = new ScriptedAgentProvider((session, turn) => [new TurnStarted(session, turn)]);
        await using var agents = Agents(bus, provider);
        var turn = await StartAsync(agents, "Add GitHub login");

        Assert.Single(provider.Sessions).End();

        await bus.WaitForAsync<TurnFinished>(_ => true, Cancellation);
        Assert.Equal(
            [new SessionEnded(turn.Session, SessionEnding.Closed), new TurnFinished(turn.Session, turn.Turn, TurnOutcome.Failed)],
            bus.Published.Where(published => published is SessionEnded or TurnFinished));
    }

    [Fact]
    public async Task ASessionThatEndsBetweenTurnsIsReportedClosedWithoutEndingATurnAgainAsync()
    {
        var bus = new RecordingBus();
        var provider = new ScriptedAgentProvider(ScriptedAgentProvider.Reply);
        await using var agents = Agents(bus, provider);
        var turn = await StartAsync(agents, "Add GitHub login");
        await bus.WaitForAsync<TurnFinished>(_ => true, Cancellation);

        Assert.Single(provider.Sessions).End();

        Assert.Equal(new SessionEnded(turn.Session, SessionEnding.Closed), await bus.WaitForAsync<SessionEnded>(_ => true, Cancellation));
        Assert.Single(bus.Published.OfType<TurnFinished>());
    }

    [Fact]
    public async Task StoppingASessionIsNotReportedAsItsEndAsync()
    {
        var bus = new RecordingBus();
        await using var agents = Agents(bus, new ScriptedAgentProvider((session, turn) => [new TurnStarted(session, turn)]));
        var turn = await StartAsync(agents, "Add GitHub login");

        Outcomes.Succeeds(await agents.StopAsync(turn.Session, Cancellation));

        Assert.DoesNotContain(bus.Published, published => published is SessionEnded or TurnFinished);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnlyAProviderThatAcceptsToolsIsGivenTheHarnessToolsAsync(bool accepts)
    {
        var provider = new ScriptedAgentProvider(ScriptedAgentProvider.Reply) { Capabilities = Declared with { AcceptsTools = accepts } };
        await using var agents = new AgentSessions(
            new SessionStarter([provider], [Canvas]),
            new RecordingBus(),
            TimeProvider.System,
            NullLogger<AgentSessions>.Instance);

        Outcomes.Succeeds(await agents.OpenAsync(Request, Cancellation));

        Assert.Equal(accepts ? [Canvas] : [], Assert.Single(provider.Sessions).Options.Tools);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    public async Task ASessionResumesOnlyWhenItsProviderCanResumeAndAcceptsTheTokenOtherwiseItStartsFreshAsync(
        bool canResume,
        bool rejects,
        bool resumed)
    {
        var token = new ResumeToken("conversation-1");
        var provider = new ScriptedAgentProvider(ScriptedAgentProvider.Reply)
        {
            Capabilities = Declared with { CanResume = canResume },
            RejectsResume = rejects,
        };
        await using var agents = Agents(new RecordingBus(), provider);

        var opened = Outcomes.Succeeds(await agents.OpenAsync(Request with { Resume = token }, Cancellation));

        Assert.Equal(resumed, opened.Resumed);
        Assert.Equal(resumed ? Option<ResumeToken>.Some(token) : Option<ResumeToken>.None, Assert.Single(provider.Sessions).Options.Resume);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AResumeTokenIssuedInATurnIsAnnouncedOnlyWhenTheProviderCanResumeAsync(bool canResume)
    {
        var token = new ResumeToken("conversation-1");
        var bus = new RecordingBus();
        var provider = new ScriptedAgentProvider((session, turn) =>
            [new TurnStarted(session, turn), new ResumeTokenIssued(session, turn, token), new TurnCompleted(session, turn, TurnOutcome.Finished)])
        {
            Capabilities = Declared with { CanResume = canResume },
        };
        await using var agents = Agents(bus, provider);

        var turn = await StartAsync(agents, "Add GitHub login");
        await bus.WaitForAsync<TurnFinished>(_ => true, Cancellation);

        Assert.Equal(
            canResume ? [new SessionResumable(turn.Session, token)] : [],
            bus.Published.OfType<SessionResumable>());
    }

    private static readonly HarnessTool Canvas = new("canvas", "Draw a canvas", "{}", ToolSurface.Canvas);

    private static AgentCapabilities Declared { get; } = new ScriptedAgentProvider(ScriptedAgentProvider.Reply).Capabilities;

    private static async Task<AgentTurn> StartAsync(AgentSessions agents, string instruction)
    {
        var opened = Outcomes.Succeeds(await agents.OpenAsync(Request, Cancellation));

        return Outcomes.Succeeds(await agents.SendAsync(opened.Session, instruction, Cancellation));
    }

    private static AgentSessions Agents(RecordingBus bus, params IAgentProvider[] providers) =>
        new(new SessionStarter(providers, []), bus, TimeProvider.System, NullLogger<AgentSessions>.Instance);
}
