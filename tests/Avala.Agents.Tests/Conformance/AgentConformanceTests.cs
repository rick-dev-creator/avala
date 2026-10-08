using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Testing;

namespace Avala.Agents.Tests.Conformance;

public sealed class AgentConformanceTests
{
    private static CancellationToken Deadline => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AWellBehavedProviderConformsAsync()
    {
        var provider = new ScriptedAgentProvider(ScriptedAgentProvider.Reply);

        Assert.Empty(await AgentConformance.CheckTurnAsync(provider, Deadline));
    }

    [Fact]
    public async Task ReportsAnItemLeftOpenAsync()
    {
        var provider = new ScriptedAgentProvider((session, turn) =>
        [
            new TurnStarted(session, turn),
            new ItemStarted(session, turn, new ItemId("build"), ItemKind.Command, "Build"),
            new TurnCompleted(session, turn, TurnOutcome.Finished),
        ]);

        Assert.Equal(["item build was left open"], await AgentConformance.CheckTurnAsync(provider, Deadline));
    }

    [Fact]
    public async Task ReportsEventsForUnknownItemsAsync()
    {
        var provider = new ScriptedAgentProvider((session, turn) =>
        [
            new TurnStarted(session, turn),
            new ItemCompleted(session, turn, new ItemId("ghost"), ItemOutcome.Succeeded),
            new TurnCompleted(session, turn, TurnOutcome.Finished),
        ]);

        Assert.Equal(["ItemCompleted was rejected: UnknownItem"], await AgentConformance.CheckTurnAsync(provider, Deadline));
    }

    [Fact]
    public async Task ReportsAMissingTurnStartAsync()
    {
        var provider = new ScriptedAgentProvider((session, turn) =>
        [
            new ItemStarted(session, turn, new ItemId("reply"), ItemKind.Message, "Reply"),
            new TurnCompleted(session, turn, TurnOutcome.Finished),
        ]);

        Assert.Equal(
            ["ItemStarted arrived before TurnStarted", "TurnCompleted arrived before TurnStarted"],
            await AgentConformance.CheckTurnAsync(provider, Deadline));
    }

    [Fact]
    public async Task ReportsATurnThatNeverEndsAsync()
    {
        var provider = new ScriptedAgentProvider((session, turn) => [new TurnStarted(session, turn)]);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Deadline);

        var check = AgentConformance.CheckTurnAsync(provider, deadline.Token);
        await deadline.CancelAsync();

        Assert.Equal(["the turn did not complete before the deadline"], await check);
    }
}
