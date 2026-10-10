using Avala.Agents.Contracts.Events;
using Avala.Permissions.Contracts;
using Avala.Testing.UI;
using Avala.Workbench.Cards;
using Avala.Workbench.Tests.Cards;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace Avala.Workbench.Tests.Views;

public sealed class PermissionCardViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AWaitingRequestSaysWhatItWantsInAmberAndShowsItsTargetInTheCodeFontAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignPermissionCardViewModel());

            Assert.Equal(("Wants to run a command", "pnpm add -D @testing-library/user-event@14.5.2"), (view.TextOf("Headline"), view.Find<SelectableTextBlock>("Target").Text));
            Assert.Equal(Color.Parse("#E5A13A"), Assert.IsAssignableFrom<ISolidColorBrush>(view.Find<TextBlock>("Headline").Foreground).Color);
            Assert.Equal("JetBrains Mono", view.Find<SelectableTextBlock>("Target").FontFamily.FamilyNames[0]);
            Assert.True(view.Shows("Allow") && view.Shows("Deny") && view.Shows("DontAskAgain"));
            Assert.Equal((false, false), (view.Shows("Resolved"), view.Shows("Error")));
        }, Cancellation);

    [Fact]
    public Task TheNoteStaysOutOfTheWayUntilAskedForAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignPermissionCardViewModel());
            var hidden = view.Shows("Note");

            view.Click("AddNote");

            Assert.Equal((false, true), (hidden, view.Shows("Note")));
        }, Cancellation);

    [Fact]
    public Task AllowingWithANoteAndDontAskAgainSendsBothAndFoldsTheCardIntoItsVerdictAsync() =>
        ui.RunAsync(async () =>
        {
            var asking = new Asking();
            var permissions = new FakePermissionAnswers();
            var card = new PermissionCardViewModel(asking.Permission(), new(permissions, new FakeAgents()));
            var view = Screen.Show(card);

            view.Click("AddNote").Type("Note", "Only the checkout tests").Click("DontAskAgain").Click("Allow");
            await (card.AllowCommand.ExecutionTask ?? Task.CompletedTask);
            card.Update(asking.Permission() with { Resolution = PermissionAnswer.Allow });
            view.Settle();

            var reply = Assert.Single(permissions.Replies).Reply;
            Assert.Equal(("Only the checkout tests", Remember.ForThisJob), (reply.Message.Match(note => note, () => string.Empty), reply.Remember));
            Assert.Equal(("Allowed", false, true), (view.TextOf("Outcome"), view.Shows("Card"), view.Shows("Resolved")));
            Assert.False(view.Shows("AlwaysInRepository"));
        }, Cancellation);

    [Fact]
    public Task AlwaysInThisRepositoryIsShownWhenOfferedAndItsAnswerSaysTheRuleAwaitsACommitAsync() =>
        ui.RunAsync(async () =>
        {
            var asking = new Asking();
            var permissions = new FakePermissionAnswers();
            var card = new PermissionCardViewModel(asking.OfferingTheRepository(), new(permissions, new FakeAgents()));
            var view = Screen.Show(card);

            view.Click("AlwaysInRepository").Click("Allow");
            await (card.AllowCommand.ExecutionTask ?? Task.CompletedTask);
            card.Update(asking.OfferingTheRepository() with { Resolution = PermissionAnswer.Allow });
            view.Settle();

            Assert.Equal(("Always in this repository", true), (view.Find<CheckBox>("AlwaysInRepository").Content, view.Find<CheckBox>("DontAskAgain").IsChecked == true));
            Assert.Equal(Remember.InThisRepository, Assert.Single(permissions.Replies).Reply.Remember);
            Assert.Equal("Added to .avala/permissions.json. It applies to new jobs once you commit it; this job won't ask again.", view.TextOf("Notice"));
        }, Cancellation);

    [Fact]
    public Task TheKeyboardAllowsWithControlEnterAndDeniesWithControlBackspaceAsync() =>
        ui.RunAsync(async () =>
        {
            var permissions = new FakePermissionAnswers();
            var allowed = new PermissionCardViewModel(new Asking().Permission(), new(permissions, new FakeAgents()));
            var view = Screen.Show(allowed);
            view.Find("DontAskAgain").Focus();
            view.Press(Key.Enter, RawInputModifiers.Control);
            await (allowed.AllowCommand.ExecutionTask ?? Task.CompletedTask);

            var denied = new PermissionCardViewModel(new Asking().Permission(), new(permissions, new FakeAgents()));
            var other = Screen.Show(denied);
            other.Find("Allow").Focus();
            other.Press(Key.Back, RawInputModifiers.Control);
            await (denied.DenyCommand.ExecutionTask ?? Task.CompletedTask);

            Assert.Equal([PermissionAnswer.Allow, PermissionAnswer.Deny], permissions.Replies.Select(reply => reply.Reply.Answer));
        }, Cancellation);

    [Fact]
    public Task ALongCommandWrapsInsideTheCardAsync() =>
        ui.RunAsync(() =>
        {
            var target = string.Join(" && ", Enumerable.Repeat("pnpm vitest run CheckoutForm --repeat 20 --reporter verbose", 6));
            var view = Screen.Show(new PermissionCardViewModel(new Asking().Permission(target), new(new FakePermissionAnswers(), new FakeAgents())));

            var shown = view.Find<SelectableTextBlock>("Target");
            Assert.True(shown.Bounds.Width <= view.Find("Card").Bounds.Width);
            Assert.True(shown.Bounds.Height > 3 * shown.LineHeight);
        }, Cancellation);
}

public sealed class FormCardViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AQuestionShowsWhatItAsksItsContextAndItsOptionsWithTheRecommendedOneChosenAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFormCardViewModel());

            Assert.Equal(("Asks a question", "Where should generated PDFs live?", "Waiting for you"), (view.TextOf("Headline"), view.TextOf("Title"), view.TextOf("Verdict")));
            Assert.Contains("Invoices render to about 80 KB. Nothing in this service stores files today.", view.VisibleTexts);
            Assert.Equal(["Render on each request"], view.All<ToggleButton>().Where(choice => choice.Name == "Choice" && choice.IsChecked == true).Select(choice => ((IFormChoiceViewModel)choice.DataContext!).Label));
            Assert.True(view.Find<Button>("Submit").IsEffectivelyEnabled);
        }, Cancellation);

    [Fact]
    public Task ChoosingAnotherOptionAndAnsweringSendsItAsync() =>
        ui.RunAsync(async () =>
        {
            var agents = new FakeAgents();
            var card = new FormCardViewModel(new Asking().Form(), new(new FakePermissionAnswers(), agents));
            var view = Screen.Show(card);

            view.All<ToggleButton>().Single(choice => choice.Name == "Choice" && Equals((choice.DataContext as IFormChoiceViewModel)?.Label, "SQLite")).IsChecked = true;
            view.Settle();
            view.Click("Submit");
            await (card.SubmitCommand.ExecutionTask ?? Task.CompletedTask);

            Assert.Equal(["SQLite"], Assert.Single(Assert.Single(agents.Answers).Answer.Fields).Chosen);
        }, Cancellation);

    [Fact]
    public Task ControlEnterAnswersFromAnywhereInTheCardAsync() =>
        ui.RunAsync(async () =>
        {
            var agents = new FakeAgents();
            var card = new FormCardViewModel(new Asking().Form(), new(new FakePermissionAnswers(), agents));
            var view = Screen.Show(card);

            view.All<ToggleButton>().First(choice => choice.Name == "Choice").Focus();
            view.Press(Key.Enter, RawInputModifiers.Control);
            await (card.SubmitCommand.ExecutionTask ?? Task.CompletedTask);

            Assert.Equal(["PostgreSQL"], Assert.Single(Assert.Single(agents.Answers).Answer.Fields).Chosen);
        }, Cancellation);

    [Fact]
    public Task AnAnsweredFormFoldsIntoItsVerdictAsync() =>
        ui.RunAsync(() =>
        {
            var asking = new Asking();
            var card = new FormCardViewModel(asking.Form(), new(new FakePermissionAnswers(), new FakeAgents()));
            var view = Screen.Show(card);

            card.Update(asking.Form() with { Closed = true });
            view.Settle();

            Assert.Equal((false, true, "No longer waiting"), (view.Shows("Card"), view.Shows("Resolved"), view.TextOf("Outcome")));
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

    [Fact]
    public Task AFieldWithoutAHeaderOrPromptShowsNeitherAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new FormFieldViewModel(new FormField("db", string.Empty, string.Empty, FieldKind.SingleChoice, [new FormOption("SQLite", "A file")])));

            Assert.Equal((false, false, true), (view.Shows("Header"), view.Shows("Prompt"), view.Shows("Choices")));
        }, Cancellation);
}

public sealed class FormChoiceViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AChoiceShowsItsLabelDescriptionAndWhetherItIsRecommendedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFormChoiceViewModel());

            Assert.Equal(("Render on each request", true, true), (view.TextOf("Label"), view.Shows("Recommended"), view.Find<ToggleButton>("Choice").IsChecked == true));
            Assert.Equal(Color.Parse("#8DA2FB"), Assert.IsAssignableFrom<ISolidColorBrush>(view.Find<Avalonia.Controls.Shapes.Ellipse>("Radio").Stroke).Color);
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

    [Fact]
    public Task SpaceChoosesTheFocusedChoiceAsync() =>
        ui.RunAsync(() =>
        {
            var choice = new FormChoiceViewModel(new FormOption("SQLite", "A file"));
            var view = Screen.Show(choice);

            view.Find("Choice").Focus();
            view.Press(Key.Space);

            Assert.True(choice.IsSelected);
        }, TestContext.Current.CancellationToken);
}
