using Avala.Agents.Contracts.Events;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Conversation;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class ReasoningViewModelScripts
{
    private static readonly DateTimeOffset Started = DateTimeOffset.UnixEpoch;

    [Fact]
    public void WhileTheAgentThinksTheBlockSaysSoAndStaysCollapsed() =>
        ViewModelScript.Given(new ReasoningViewModel(new ReasoningEntry("r", "Invoice.total() rounds", Started, Option<TimeSpan>.None, Option<ItemOutcome>.None)))
            .Then(reasoning => Assert.Equal(("Thinking", true, false, TimeSpan.Zero), (reasoning.Summary, reasoning.IsThinking, reasoning.IsExpanded, reasoning.Duration)));

    [Theory]
    [InlineData(12, "Thought for 12s")]
    [InlineData(0.2, "Thought for 1s")]
    [InlineData(154, "Thought for 2m 34s")]
    public void FinishedThinkingSaysForHowLong(double seconds, string summary) =>
        ViewModelScript.Given(new ReasoningViewModel(new ReasoningEntry("r", string.Empty, Started, Option<TimeSpan>.None, Option<ItemOutcome>.None)))
            .When(reasoning => reasoning.Update(new ReasoningEntry("r", "Money.round knows the currency", Started, TimeSpan.FromSeconds(seconds), ItemOutcome.Succeeded)))
            .ThenNotified(nameof(ReasoningViewModel.Summary), nameof(ReasoningViewModel.IsThinking))
            .Then(reasoning => Assert.Equal((summary, false), (reasoning.Summary, reasoning.IsThinking)));

    [Fact]
    public void TheThoughtExpandsOnDemandAndKeepsStreamingWhileOpen() =>
        ViewModelScript.Given(new ReasoningViewModel(new ReasoningEntry("r", "JPY has", Started, Option<TimeSpan>.None, Option<ItemOutcome>.None)))
            .Invoke(nameof(ReasoningViewModel.ToggleCommand))
            .When(reasoning => reasoning.Update(new ReasoningEntry("r", "JPY has zero decimals", Started, Option<TimeSpan>.None, Option<ItemOutcome>.None)))
            .Then(reasoning => Assert.Equal((true, "JPY has zero decimals"), (reasoning.IsExpanded, reasoning.Text)));
}
