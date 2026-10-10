using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Simulator.Scenarios;
using Avala.Testing;

namespace Avala.Simulator.Tests.Playback;

public sealed class ChildAnsweringTests
{
    private const string AskingChild = "[simulate: delegate-asks-parent] Migrate the database";

    private static readonly HarnessTool Delegating = new(ScenarioCatalog.Delegate, "Delegate", "{}", ToolSurface.Executed);

    private static readonly HarnessTool Answering = new(ScenarioCatalog.AnswerChild, "Answer a child", "{}", ToolSurface.Executed);

    private static readonly HarnessTool Waiting = new(ScenarioCatalog.WaitChild, "Wait for a child", "{}", ToolSurface.Executed);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static ToolCalled Called(IReadOnlyList<IAgentEvent> events) => Assert.IsType<ToolCalled>(events[^1]);

    private static async Task<ToolCalled> ReturnAsync(Stage stage, ToolCalled called, string content)
    {
        Outcomes.Succeeds(await stage.Session.ReturnAsync(new ToolResult(called.Item, content), Cancellation));

        return Called(await stage.ReadUntilAsync<ToolCalled>(Cancellation));
    }

    [Fact]
    public async Task AChildRequestHeardMidCallIsAnsweredWithTheScenarioDecisionAsync()
    {
        await using var stage = new Stage(PermissionMode.AllowAll, Delegating, Answering);
        await stage.SendAsync(AskingChild, Cancellation);
        var delegated = Called(await stage.ReadUntilAsync<ToolCalled>(Cancellation));

        Outcomes.Succeeds(await stage.Session.SendAsync(new UserTurn("Sub-agent \"Migrate\" is waiting for you\nanswer_child {\"child\":\"7\",\"request\":\"r1\",\"decision\":\"deny\"}") { MidTurn = true }, Cancellation));
        var noted = await stage.ReadUntilAsync<ToolCalled>(Cancellation);
        var answered = Called(noted);
        Outcomes.Succeeds(await stage.Session.ReturnAsync(new ToolResult(answered.Item, "Allowed."), Cancellation));
        Outcomes.Succeeds(await stage.Session.ReturnAsync(new ToolResult(delegated.Item, "Migrated."), Cancellation));
        var events = await stage.ReadTurnAsync(Cancellation);

        Assert.Contains(noted, agentEvent => agentEvent is ItemProgressed { Item.Value: "noted-1", Text: "I am folding it into this turn." });
        Assert.Equal((ScenarioCatalog.AnswerChild, """{"child":"7","request":"r1","decision":"allow"}"""), (answered.Tool, answered.Input));
        Assert.Contains(new ItemCompleted(stage.Session.Id, answered.Turn, answered.Item, ItemOutcome.Succeeded), events);
        Assert.Contains(events, agentEvent => agentEvent is ItemProgressed { Text: "The harness answered: Migrated." });
    }

    [Theory]
    [InlineData("Keep going")]
    [InlineData("answer_child not json")]
    [InlineData("answer_child [1]")]
    public async Task AMessageMidCallWithoutAChildRequestIsOnlyNotedAsync(string message)
    {
        await using var stage = new Stage(PermissionMode.AllowAll, Delegating, Answering);
        await stage.SendAsync(AskingChild, Cancellation);
        var delegated = Called(await stage.ReadUntilAsync<ToolCalled>(Cancellation));

        Outcomes.Succeeds(await stage.Session.SendAsync(new UserTurn(message) { MidTurn = true }, Cancellation));
        await stage.ReadUntilAsync<ItemCompleted>(Cancellation);
        Outcomes.Succeeds(await stage.Session.ReturnAsync(new ToolResult(delegated.Item, "Migrated."), Cancellation));
        var events = await stage.ReadTurnAsync(Cancellation);

        Assert.DoesNotContain(events, agentEvent => agentEvent is ToolCalled);
        Assert.Contains(events, agentEvent => agentEvent is ItemProgressed { Text: "The harness answered: Migrated." });
    }

    [Fact]
    public async Task ACallThatReturnsEarlyWithAChildRequestIsAnsweredAndTheChildIsAwaitedAgainAsync()
    {
        await using var stage = new Stage(PermissionMode.AllowAll, Delegating, Answering, Waiting);
        await stage.SendAsync(AskingChild, Cancellation);
        var delegated = Called(await stage.ReadUntilAsync<ToolCalled>(Cancellation));

        var answered = await ReturnAsync(stage, delegated, """{"outcome":"asking","answer_child":{"child":"7","request":"r1","decision":"deny"},"wait_child":{"child":"7"}}""");
        var waited = await ReturnAsync(stage, answered, "Allowed.");
        Outcomes.Succeeds(await stage.Session.ReturnAsync(new ToolResult(waited.Item, "Migrated."), Cancellation));
        var events = await stage.ReadTurnAsync(Cancellation);

        Assert.Equal((ScenarioCatalog.AnswerChild, """{"child":"7","request":"r1","decision":"allow"}"""), (answered.Tool, answered.Input));
        Assert.Equal((ScenarioCatalog.WaitChild, """{"child":"7"}"""), (waited.Tool, waited.Input));
        Assert.Contains(events, agentEvent => agentEvent is ItemProgressed { Text: "The harness answered: Migrated." });
    }

    [Fact]
    public async Task ACallThatReturnsEarlyWithoutAChildRequestOnlyAwaitsTheChildAgainAsync()
    {
        await using var stage = new Stage(PermissionMode.AllowAll, Delegating, Answering, Waiting);
        await stage.SendAsync(AskingChild, Cancellation);
        var delegated = Called(await stage.ReadUntilAsync<ToolCalled>(Cancellation));

        var waited = await ReturnAsync(stage, delegated, """{"outcome":"waiting","wait_child":{"child":"7"}}""");
        Outcomes.Succeeds(await stage.Session.ReturnAsync(new ToolResult(waited.Item, "Migrated."), Cancellation));
        var events = await stage.ReadTurnAsync(Cancellation);

        Assert.Equal((ScenarioCatalog.WaitChild, """{"child":"7"}"""), (waited.Tool, waited.Input));
        Assert.DoesNotContain(events, agentEvent => agentEvent is ToolCalled);
        Assert.Contains(events, agentEvent => agentEvent is ItemProgressed { Text: "The harness answered: Migrated." });
    }

    [Theory]
    [InlineData("Migrated.")]
    [InlineData("""{"outcome":"done"}""")]
    [InlineData("[1]")]
    public async Task AResultThatIsNotAnEarlyReturnIsTheAnswerEvenWhenTheChildCanBeAwaitedAsync(string result)
    {
        await using var stage = new Stage(PermissionMode.AllowAll, Delegating, Waiting);
        await stage.SendAsync(AskingChild, Cancellation);
        var delegated = Called(await stage.ReadUntilAsync<ToolCalled>(Cancellation));

        Outcomes.Succeeds(await stage.Session.ReturnAsync(new ToolResult(delegated.Item, result), Cancellation));
        var events = await stage.ReadTurnAsync(Cancellation);

        Assert.DoesNotContain(events, agentEvent => agentEvent is ToolCalled);
        Assert.Contains(events, agentEvent => agentEvent is ItemProgressed { Text: var text } && text == $"The harness answered: {result}");
    }
}
