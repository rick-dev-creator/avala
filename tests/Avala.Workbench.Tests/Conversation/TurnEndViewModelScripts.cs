using Avala.Agents.Contracts.Events;
using Avala.Testing;
using Avala.Workbench.Conversation;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class TurnEndViewModelScripts
{
    [Theory]
    [InlineData("Finished", 154, "Worked for 2m 34s")]
    [InlineData("Interrupted", 9, "Interrupted after 9s")]
    [InlineData("Failed", 61, "Failed after 1m 1s")]
    public void TheEndOfATurnSaysHowItEndedAndHowLongItTook(string outcome, int seconds, string summary) =>
        ViewModelScript.Given(new TurnEndViewModel(new TurnEndEntry("turn", Enum.Parse<TurnOutcome>(outcome), TimeSpan.FromSeconds(seconds), default, [])))
            .Then(turn => Assert.Equal((summary, 0L, string.Empty), (turn.Summary, turn.Tokens, turn.Cost)));

    [Fact]
    public void TheEndOfATurnSumsItsTokensAndListsEachCurrency() =>
        ViewModelScript.Given(new TurnEndViewModel(new TurnEndEntry("turn", TurnOutcome.Finished, TimeSpan.FromSeconds(30), new TokenUsage(40_000, 8_000, 200, 10, 0), [new Cost(0.412m, "USD"), new Cost(61m, "JPY")])))
            .Then(turn => Assert.Equal((48_210L, "0.412 USD + 61 JPY"), (turn.Tokens, turn.Cost)));
}
