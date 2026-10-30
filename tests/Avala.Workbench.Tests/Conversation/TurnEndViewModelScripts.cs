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
    [InlineData("LimitReached", 2, "Ran out of its usage window after 2s")]
    public void TheEndOfATurnSaysHowItEndedAndHowLongItTook(string outcome, int seconds, string summary) =>
        ViewModelScript.Given(new TurnEndViewModel(new TurnEndEntry("turn", Enum.Parse<TurnOutcome>(outcome), TimeSpan.FromSeconds(seconds), default, [])))
            .Then(turn => Assert.Equal((summary, 0L, string.Empty), (turn.Summary, turn.Tokens, turn.Cost)));

    [Fact]
    public void TheEndOfATurnSumsItsTokensAndListsEachCurrencySayingWhatIsAtApiPrices() =>
        ViewModelScript.Given(new TurnEndViewModel(new TurnEndEntry("turn", TurnOutcome.Finished, TimeSpan.FromSeconds(30), new TokenUsage(40_000, 8_000, 200, 10, 0), [new Cost(0.412m, "USD") { Basis = CostBasis.ApiEquivalent }, new Cost(61m, "JPY")])))
            .Then(turn => Assert.Equal((48_210L, "0.41 USD at API prices + 61 JPY"), (turn.Tokens, turn.Cost)));

    [Fact]
    public void TheBreakdownOfATurnListsItsTokensByTypeTheModelAndTheCostOfTheWholeTurn() =>
        ViewModelScript.Given(new TurnEndViewModel(new TurnEndEntry("turn", TurnOutcome.Finished, TimeSpan.FromSeconds(30), new TokenUsage(40_000, 8_000, 200, 10, 0), [new Cost(0.412m, "USD") { Basis = CostBasis.ApiEquivalent }]) { Model = "claude-opus-5-5" }))
            .Then(turn => Assert.Equal(
                (true, "Input: 40,000 tokens\nOutput: 8,000 tokens\nCache read: 200 tokens\nCache write: 10 tokens\nModel: claude-opus-5-5\nCost of the whole turn: 0.41 USD at API prices"),
                (turn.HasUsage, turn.Breakdown)));

    [Fact]
    public void ATurnWhoseHarnessReportedNoUsageSaysSoInsteadOfCountingZero() =>
        ViewModelScript.Given(new TurnEndViewModel(new TurnEndEntry("turn", TurnOutcome.Finished, TimeSpan.FromSeconds(3), default, [])))
            .Then(turn => Assert.Equal((false, "The harness reported no usage for this turn."), (turn.HasUsage, turn.Breakdown)));
}
