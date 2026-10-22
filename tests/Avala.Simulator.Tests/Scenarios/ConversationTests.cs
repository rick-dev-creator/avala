using Avala.Simulator.Scenarios;

namespace Avala.Simulator.Tests.Scenarios;

public sealed class ConversationTests
{
    [Fact]
    public void AConversationReadsAsItsScenarioAndTheTurnsPlayedInsteadOfRecursingIntoItsNextTurn() =>
        Assert.Contains("reply", Conversation.Begin(ScenarioCatalog.Reply, "work").Advanced.ToString(), StringComparison.Ordinal);
}
