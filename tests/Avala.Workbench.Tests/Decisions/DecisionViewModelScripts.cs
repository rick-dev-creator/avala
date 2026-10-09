using Avala.Agents.Contracts.Events;
using Avala.Testing;
using Avala.Workbench.Cards;
using Avala.Workbench.Decisions;
using Avala.Workbench.Tests.Cards;

namespace Avala.Workbench.Tests.Decisions;

public sealed class DecisionViewModelScripts
{
    private static readonly DateTimeOffset Since = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);
    private readonly Asking asking = new();

    [Theory]
    [InlineData(20, "waiting less than a minute")]
    [InlineData(300, "waiting 5m")]
    [InlineData(4500, "waiting 1h 15m")]
    public void ADecisionTellsHowLongItHasWaited(int seconds, string waiting) =>
        ViewModelScript.Given(Decision())
            .When(decision => decision.Update(asking.Permission(), Since.AddSeconds(seconds)))
            .ThenNotified(nameof(DecisionViewModel.Waiting))
            .Then(decision => Assert.Equal(waiting, decision.Waiting));

    [Fact]
    public void ADecisionCarriesItsJobAndTheCardItIsAnsweredWith() =>
        ViewModelScript.Given(Decision())
            .Then(decision => Assert.Equal(("Fix flaky CheckoutForm test", "Run the CheckoutForm tests"), (decision.JobTitle, Assert.IsType<PermissionCardViewModel>(decision.Card).Title)));

    [Fact]
    public void AnUpdateReachesItsCard() =>
        ViewModelScript.Given(Decision())
            .When(decision => decision.Update(asking.Permission() with { Resolution = PermissionAnswer.Deny }, Since))
            .Then(decision => Assert.Equal("Denied", Assert.IsType<PermissionCardViewModel>(decision.Card).Verdict));

    private DecisionViewModel Decision() =>
        new(Jobs.Contracts.JobId.New(), "Fix flaky CheckoutForm test", new PermissionCardViewModel(asking.Permission(), new(new FakePermissionAnswers(), new FakeAgents())), Since);
}
