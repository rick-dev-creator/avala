using Avala.Agents.Contracts.Events;
using Avala.Testing.UI;
using Avala.Workbench.Cards;
using Avala.Workbench.Tests.Cards;
using Avalonia.Controls;
using Avalonia.Media;

namespace Avala.Workbench.Tests.Views;

public sealed class PermissionCardViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AWaitingRequestShowsItsTargetInTheCodeFontWithAnAmberEdgeAndItsAnswersAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignPermissionCardViewModel());

            Assert.Equal("npm test -- CheckoutForm.test.tsx --runInBand", view.Find<SelectableTextBlock>("Target").Text);
            Assert.Equal("JetBrains Mono", view.Find<SelectableTextBlock>("Target").FontFamily.FamilyNames[0]);
            Assert.Equal(Color.Parse("#59E5A13A"), Assert.IsAssignableFrom<ISolidColorBrush>(view.Find<Border>("Card").BorderBrush).Color);
            Assert.True(view.Shows("Allow") && view.Shows("Deny") && view.Shows("Note"));
        }, Cancellation);

    [Fact]
    public Task AllowingFromTheCardSendsTheTypedNoteAndHidesTheAnswersOnceResolvedAsync() =>
        ui.RunAsync(async () =>
        {
            var asking = new Asking();
            var permissions = new FakePermissionAnswers();
            var card = new PermissionCardViewModel(asking.Permission(), new(permissions, new FakeAgents()));
            var view = Screen.Show(card);

            view.Type("Note", "Only the checkout tests");
            view.Click("Allow");
            await (card.AllowCommand.ExecutionTask ?? Task.CompletedTask);
            card.Update(asking.Permission() with { Resolution = PermissionAnswer.Allow });
            view.Settle();

            Assert.Equal("Only the checkout tests", Assert.Single(permissions.Replies).Reply.Message.Match(note => note, () => string.Empty));
            Assert.Equal(("Allowed", false), (view.TextOf("Verdict"), view.Shows("Answer")));
        }, Cancellation);
}

public sealed class FormCardViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AFormShowsItsQuestionFieldsAndAnswersWithTheRecommendedOptionChosenAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFormCardViewModel());

            Assert.Equal(("Add invoice PDF endpoint", "Waiting for you"), (view.TextOf("Title"), view.TextOf("Verdict")));
            Assert.Contains("GET /invoices/{id}/pdf", view.VisibleTexts);
            Assert.Contains("recommended", view.VisibleTexts);
            Assert.True(view.Find<Button>("Submit").IsEffectivelyEnabled);
        }, Cancellation);

    [Fact]
    public Task ChoosingAnotherOptionAndAnsweringSendsItAsync() =>
        ui.RunAsync(async () =>
        {
            var agents = new FakeAgents();
            var card = new FormCardViewModel(new Asking().Form(), new(new FakePermissionAnswers(), agents));
            var view = Screen.Show(card);

            view.All<CheckBox>().Single(box => box.Name == "Choice" && Equals((box.DataContext as IFormChoiceViewModel)?.Label, "SQLite")).IsChecked = true;
            view.Settle();
            view.Click("Submit");
            await (card.SubmitCommand.ExecutionTask ?? Task.CompletedTask);

            Assert.Equal(["SQLite"], Assert.Single(Assert.Single(agents.Answers).Answer.Fields).Chosen);
        }, Cancellation);
}

public sealed class FormFieldViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AChoiceFieldOffersItsOptionsAndNoConfirmationAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new FormFieldViewModel(Asking.Database[0]));

            Assert.Equal((true, false, false), (view.Shows("Choices"), view.Shows("Confirmed"), view.Shows("Text")));
        }, Cancellation);

    [Fact]
    public Task AFreeTextFieldOffersATextBoxEvenWithoutDeclaringFreeTextAsync() =>
        ui.RunAsync(() =>
        {
            var field = new FormFieldViewModel(new FormField("why", "Reason", "Why rate-limit by address?", FieldKind.FreeText, []));
            var view = Screen.Show(field);

            view.Type("Text", "Shared offices");

            Assert.Equal((true, false, false), (view.Shows("Text"), view.Shows("Confirmed"), view.Shows("Choices")));
            Assert.Equal(("Shared offices", true), (field.Text, field.IsComplete));
        }, Cancellation);

    [Fact]
    public Task AConfirmationFieldAsksForTheConfirmationAloneAsync() =>
        ui.RunAsync(() =>
        {
            var field = new FormFieldViewModel(new FormField("approve", "Plan", "Proceed with this plan?", FieldKind.Confirmation, []));
            var view = Screen.Show(field);

            view.Click("Confirmed");

            Assert.Equal((true, false), (view.Shows("Confirmed"), view.Shows("Text")));
            Assert.True(field.Confirmed);
        }, Cancellation);
}

public sealed class FormChoiceViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AChoiceShowsItsLabelDescriptionAndWhetherItIsRecommendedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFormChoiceViewModel());

            Assert.Equal(("GET /invoices/{id}/pdf", true, true), (view.TextOf("Label"), view.Shows("Recommended"), view.Find<CheckBox>("Choice").IsChecked == true));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ClickingAChoiceChoosesItAsync() =>
        ui.RunAsync(() =>
        {
            var choice = new FormChoiceViewModel(new FormOption("GET /invoices/{id}?format=pdf", string.Empty));
            var view = Screen.Show(choice);

            view.Click("Choice");

            Assert.Equal((true, false, false), (choice.IsSelected, view.Shows("Recommended"), view.Shows("Description")));
        }, TestContext.Current.CancellationToken);
}
