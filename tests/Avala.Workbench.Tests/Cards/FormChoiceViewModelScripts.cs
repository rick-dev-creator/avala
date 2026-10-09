using Avala.Agents.Contracts.Events;
using Avala.Testing;
using Avala.Workbench.Cards;

namespace Avala.Workbench.Tests.Cards;

public sealed class FormChoiceViewModelScripts
{
    [Fact]
    public void ARecommendedOptionStartsChosen() =>
        ViewModelScript.Given(new FormChoiceViewModel(new FormOption("GET /invoices/{id}/pdf", "A resource of its own", Recommended: true)))
            .Then(choice => Assert.Equal(("GET /invoices/{id}/pdf", "A resource of its own", true, true), (choice.Label, choice.Description, choice.Recommended, choice.IsSelected)));

    [Fact]
    public void AnOtherOptionStartsUnchosenAndTellsWhenChosen() =>
        ViewModelScript.Given(new FormChoiceViewModel(new FormOption("GET /invoices/{id}?format=pdf", "Content negotiation")))
            .Then(choice => Assert.False(choice.IsSelected))
            .When(choice => choice.IsSelected = true)
            .ThenNotified(nameof(FormChoiceViewModel.IsSelected))
            .Then(choice => Assert.True(choice.IsSelected));
}
