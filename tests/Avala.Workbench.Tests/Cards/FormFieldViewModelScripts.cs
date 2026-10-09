using Avala.Agents.Contracts.Events;
using Avala.Testing;
using Avala.Workbench.Cards;

namespace Avala.Workbench.Tests.Cards;

public sealed class FormFieldViewModelScripts
{
    [Fact]
    public void ChoosingAnotherOptionOfASingleChoiceUnselectsTheFirst() =>
        ViewModelScript.Given(new FormFieldViewModel(Asking.Database[0]))
            .When(field => field.Choices[1].IsSelected = true)
            .ThenNotified(nameof(FormFieldViewModel.IsComplete))
            .Then(field =>
            {
                Assert.Equal([false, true], field.Choices.Select(choice => choice.IsSelected));
                Assert.Equal(["SQLite"], field.Choice().Chosen);
            });

    [Fact]
    public void AMultipleChoiceKeepsEveryChosenOption() =>
        ViewModelScript.Given(new FormFieldViewModel(new FormField("steps", "Steps", "Which steps?", FieldKind.MultipleChoice, [new FormOption("Address", string.Empty), new FormOption("Payment", string.Empty)])))
            .When(field =>
            {
                field.Choices[0].IsSelected = true;
                field.Choices[1].IsSelected = true;
            })
            .Then(field => Assert.Equal(["Address", "Payment"], field.Choice().Chosen));

    [Fact]
    public void AFreeTextFieldTakesTextEvenWhenItDoesNotDeclareFreeText() =>
        ViewModelScript.Given(new FormFieldViewModel(new FormField("why", "Reason", "Why rate-limit by address?", FieldKind.FreeText, [])))
            .Then(field => Assert.Equal((true, false, false), (field.AcceptsText, field.IsConfirmation, field.IsComplete)))
            .When(field => field.Text = "Shared offices share an address")
            .Then(field => Assert.True(field.IsComplete));

    [Fact]
    public void OnlyAConfirmationFieldAsksForAConfirmation()
    {
        var confirmation = new FormFieldViewModel(new FormField("approve", "Plan", "Proceed with this plan?", FieldKind.Confirmation, []));
        var choice = new FormFieldViewModel(Asking.Database[0]);

        Assert.Equal((true, false), (confirmation.IsConfirmation, choice.IsConfirmation));
        Assert.False(choice.AcceptsText);
    }

    [Fact]
    public void ASingleChoiceThatAcceptsTextIsCompleteWithTextInsteadOfAnOption() =>
        ViewModelScript.Given(new FormFieldViewModel(Asking.Choice(new FormOption("PostgreSQL", "Relational"), new FormOption("SQLite", "A file")) with { AcceptsFreeText = true }))
            .Then(field => Assert.False(field.IsComplete))
            .When(field => field.Text = "CockroachDB")
            .ThenNotified(nameof(FormFieldViewModel.IsComplete))
            .Then(field => Assert.True(field.IsComplete));
}
