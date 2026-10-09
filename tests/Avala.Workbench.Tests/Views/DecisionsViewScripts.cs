using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing.UI;
using Avala.Workbench.Board;
using Avala.Workbench.Cards;
using Avala.Workbench.Decisions;
using Avala.Workbench.Timeline;
using Avalonia.Controls;
using Avalonia.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class DecisionsViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task EveryWaitingDecisionIsListedWithItsCardAndTheKeysThatAnswerItAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignDecisionsViewModel());

            Assert.Equal(2, view.Find<ListBox>("Items").ItemCount);
            Assert.False(view.Shows("Empty"));
            Assert.Superset(new HashSet<string>(["Fix flaky CheckoutForm test", "waiting 4m", "Add invoice PDF endpoint", "Ctrl+Enter answer"]), view.VisibleTexts.ToHashSet());
        }, Cancellation);

    [Fact]
    public Task WithNothingWaitingThePopoverSaysSoAndOffersNoAnswerAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var view = Screen.Show(bench.Decisions());

            Assert.Equal((true, false, false), (view.Shows("Empty"), view.Shows("Items"), view.Shows("Answer")));
            Assert.Equal("Nothing needs you", view.TextOf("Empty"));
        }, Cancellation);

    [Fact]
    public Task TheKeyboardMovesToTheFormChoosesItsSecondOptionAndAnswersAsync() =>
        ui.RunAsync(async () =>
        {
            using var bench = new Bench();
            var decisions = Waiting(bench);
            var view = Screen.Show(decisions);
            view.Find<ListBox>("Items").ContainerFromIndex(0)!.Focus();

            view.Press(Key.Down);
            view.Press(Key.D2);
            view.Press(Key.Enter, RawInputModifiers.Control);
            await (decisions.AnswerCommand.ExecutionTask ?? Task.CompletedTask);

            Assert.Equal(["GET /invoices/{id}?format=pdf"], Assert.Single(Assert.Single(bench.Agents.Answers).Answer.Fields).Chosen);
        }, Cancellation);

    [Fact]
    public Task TypingADigitInTheNoteWritesItInsteadOfChoosingAnOptionAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var decisions = Waiting(bench);
            decisions.MoveNextCommand.Execute(null);
            var view = Screen.Show(decisions);
            view.Find<TextBox>("DecisionNote").Focus();

            view.Press(Key.D2);

            var card = Assert.IsType<FormCardViewModel>(decisions.Items[1].Card);
            Assert.Equal([true, false], card.Fields[0].Choices.Select(choice => choice.IsSelected));
        }, Cancellation);

    [Fact]
    public Task DeletingAWordOfTheNoteDoesNotDenyTheDecisionAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var decisions = Waiting(bench);
            var view = Screen.Show(decisions);
            view.Type("DecisionNote", "Only the checkout tests");

            view.Press(Key.Back, RawInputModifiers.Control);

            Assert.Empty(bench.Permissions.Replies);
            Assert.Equal("Only the checkout ", decisions.Note);
        }, Cancellation);

    private static DecisionsViewModel Waiting(Bench bench)
    {
        var decisions = bench.Decisions();
        decisions.Show(Bench.Of(Asking(bench), Questioning(bench)));

        return decisions;
    }

    private static BoardJob Asking(Bench bench)
    {
        var session = SessionId.New();
        var turn = TurnId.New();
        var asked = bench.Time.GetUtcNow().AddMinutes(-4);

        return Bench.OnBoard(bench.Job("Fix flaky CheckoutForm test", JobStatus.Running)) with
        {
            Transcript = Transcript.Empty
                .Apply(new TurnStarted(session, turn), asked)
                .Apply(new PermissionRequested(session, turn, new ItemId("run"), "Run the CheckoutForm tests", ItemKind.Command, "npm test"), asked)
                .Apply(new PolicyDecision(session, turn, new ItemId("run"), Option<JobId>.None, ItemKind.Command, "npm test", PolicyAnswer.Ask, Option<PolicyRule>.None, DecisionDelivery.LeftToHuman, asked)),
        };
    }

    private static BoardJob Questioning(Bench bench)
    {
        var session = SessionId.New();
        var turn = TurnId.New();
        var asked = bench.Time.GetUtcNow();
        var form = new AgentForm(
            FormPurpose.Question,
            "Where should the PDF live?",
            string.Empty,
            [new FormField("route", "Endpoint", "Which route?", FieldKind.SingleChoice, [new FormOption("GET /invoices/{id}/pdf", string.Empty, Recommended: true), new FormOption("GET /invoices/{id}?format=pdf", string.Empty)])]);

        return Bench.OnBoard(bench.Job("Add invoice PDF endpoint", JobStatus.Running)) with
        {
            Transcript = Transcript.Empty
                .Apply(new TurnStarted(session, turn), asked)
                .Apply(new FormRequested(session, turn, new ItemId("question"), form), asked)
                .Apply(new FormDecision(session, turn, new ItemId("question"), Option<JobId>.None, form, Autonomy.Supervised, Option<FormAnswer>.None, [], DecisionDelivery.LeftToHuman, asked)),
        };
    }
}

public sealed class DecisionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ADecisionShowsItsJobHowLongItWaitedAndItsCardAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignDecisionViewModel());

            Assert.Equal(("Fix flaky CheckoutForm test", "waiting 4m"), (view.TextOf("JobTitle"), view.TextOf("Waiting")));
            Assert.Contains(view.All<UserControl>(), control => control.GetType().Name == "PermissionCardView");
        }, TestContext.Current.CancellationToken);
}
