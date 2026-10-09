using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Cards;
using Avala.Workbench.Decisions;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Tests.Decisions;

public sealed class DecisionsViewModelScripts : IDisposable
{
    private readonly Bench bench = new();
    private readonly SessionId permissionSession = SessionId.New();
    private readonly SessionId formSession = SessionId.New();

    [Fact]
    public void EveryWaitingDecisionAcrossJobsIsListedOldestFirstWithItsWaitAndNothingElse()
    {
        var decisions = bench.Decisions();
        var empty = (decisions.IsEmpty, decisions.Empty);

        decisions.Show(Bench.Of(Asking(), Questioning(), Bench.OnBoard(bench.Job("Update lodash to 4.17.21", JobStatus.Running))));

        Assert.Equal((true, "Nothing needs you"), empty);
        Assert.Equal(
            [("Fix flaky CheckoutForm test", "5m", typeof(PermissionCardViewModel)), ("Add invoice PDF endpoint", "<1m", typeof(FormCardViewModel))],
            decisions.Items.Select(item => (item.JobTitle, item.Waiting, item.Card.GetType())));
        Assert.Equal((false, decisions.Items[0]), (decisions.IsEmpty, decisions.Selected));
    }

    [Fact]
    public async Task TheKeyboardMovesBetweenDecisionsChoosesAnOptionAndAnswers()
    {
        var decisions = bench.Decisions();
        decisions.Show(Bench.Of(Asking(), Questioning()));
        decisions.Note = "Only the checkout tests";

        await decisions.AnswerCommand.ExecuteAsync(null);
        decisions.MoveNextCommand.Execute(null);
        var atLast = decisions.MoveNextCommand.CanExecute(null);
        decisions.ChooseCommand.Execute("2");
        await decisions.AnswerCommand.ExecuteAsync(null);

        var (session, reply) = Assert.Single(bench.Permissions.Replies);
        Assert.Equal((permissionSession, PermissionAnswer.Allow, Option<string>.Some("Only the checkout tests")), (session, reply.Answer, reply.Message));
        Assert.False(atLast);
        var (answered, answer) = Assert.Single(bench.Agents.Answers);
        Assert.Equal((formSession, "GET /invoices/{id}?format=pdf"), (answered, string.Join(",", Assert.Single(answer.Fields).Chosen)));
    }

    [Fact]
    public async Task DenyingAFormDeclinesItWithTheNote()
    {
        var decisions = bench.Decisions();
        decisions.Show(Bench.Of(Questioning()));
        decisions.Note = "Ask the team first";

        await decisions.DenyCommand.ExecuteAsync(null);

        var answer = Assert.Single(bench.Agents.Answers).Answer;
        Assert.Equal((true, Option<string>.Some("Ask the team first")), (answer.Declined, answer.Message));
        Assert.Empty(decisions.Note);
    }

    [Fact]
    public void ChoosingAnOptionAppliesOnlyToAFormAndOnlyToAnOptionItHas()
    {
        var decisions = bench.Decisions();
        decisions.Show(Bench.Of(Asking(), Questioning()));
        var onPermission = decisions.ChooseCommand.CanExecute("1");
        decisions.MoveNextCommand.Execute(null);

        decisions.ChooseCommand.Execute("9");

        var card = Assert.IsType<FormCardViewModel>(decisions.Items[1].Card);
        Assert.False(onPermission);
        Assert.Equal([true, false], card.Fields[0].Choices.Select(choice => choice.IsSelected));
    }

    [Fact]
    public void AtTheFirstDecisionThereIsNothingBeforeIt() =>
        ViewModelScript.Given(bench.Decisions())
            .When(decisions => decisions.Show(Bench.Of(Asking(), Questioning())))
            .Then(decisions => Assert.Equal((false, true), (decisions.MovePreviousCommand.CanExecute(null), decisions.MoveNextCommand.CanExecute(null))));

    [Fact]
    public void ADecisionAnsweredElsewhereLeavesThePopoverAndTheSelectionMovesOn()
    {
        var decisions = bench.Decisions();
        var asking = Asking();
        decisions.Show(Bench.Of(asking, Questioning()));

        decisions.Show(Bench.Of(asking with { Transcript = Transcript.Empty }, Questioning()));

        Assert.Equal(["Add invoice PDF endpoint"], decisions.Items.Select(item => item.JobTitle));
        Assert.Same(decisions.Items[0], decisions.Selected);
    }

    [Fact]
    public void TheSelectionStaysOnItsDecisionWhenTheBoardMoves()
    {
        var decisions = bench.Decisions();
        var questioning = Questioning();
        decisions.Show(Bench.Of(Asking(), questioning));
        decisions.MoveNextCommand.Execute(null);
        var selected = decisions.Selected;

        decisions.Show(Bench.Of(Asking(), questioning));

        Assert.Same(selected, decisions.Selected);
    }

    [Fact]
    public void RefreshingTellsHowLongEachDecisionHasWaitedSinceThen()
    {
        var decisions = bench.Decisions();
        decisions.Show(Bench.Of(Asking()));

        bench.Time.Advance(TimeSpan.FromMinutes(70));
        decisions.Refresh();

        Assert.Equal("1h 15m", decisions.Items[0].Waiting);
    }

    [Fact]
    public void WithNothingWaitingNoAnswerCanBeGiven() =>
        ViewModelScript.Given(bench.Decisions())
            .When(decisions => decisions.Show(Bench.Of(Bench.OnBoard(bench.Job("Update lodash to 4.17.21", JobStatus.Running)))))
            .Then(decisions => Assert.Equal((true, false, false), (decisions.IsEmpty, decisions.AnswerCommand.CanExecute(null), decisions.DenyCommand.CanExecute(null))));

    [Fact]
    public void TheHeaderCountsWhatIsPending()
    {
        var decisions = bench.Decisions();
        var asking = Asking();
        decisions.Show(Bench.Of(asking, Questioning()));
        var pending = decisions.Pending;

        decisions.Show(Bench.Of(asking with { Transcript = Transcript.Empty }));

        Assert.Equal(("2 pending", "all answered", true), (pending, decisions.Pending, decisions.IsEmpty));
    }

    [Fact]
    public void OnlyTheSelectedDecisionIsOpen()
    {
        var decisions = bench.Decisions();
        decisions.Show(Bench.Of(Asking(), Questioning()));
        var first = decisions.Items.Select(item => item.IsSelected).ToList();

        decisions.MoveNextCommand.Execute(null);

        Assert.Equal([true, false], first);
        Assert.Equal([false, true], decisions.Items.Select(item => item.IsSelected));
    }

    [Fact]
    public async Task ANoteBelongsToTheSelectedDecisionAndIsDroppedWhenMovingOn()
    {
        var decisions = bench.Decisions();
        decisions.Show(Bench.Of(Asking(), Questioning()));
        decisions.WriteNoteCommand.Execute(null);
        decisions.Note = "Only the checkout tests";
        var writing = decisions.IsWritingNote;

        decisions.MoveNextCommand.Execute(null);
        decisions.MovePreviousCommand.Execute(null);
        await decisions.AnswerCommand.ExecuteAsync(null);

        Assert.True(writing);
        Assert.Equal((string.Empty, false), (decisions.Note, decisions.IsWritingNote));
        Assert.Equal(Option<string>.None, Assert.Single(bench.Permissions.Replies).Reply.Message);
    }

    [Fact]
    public async Task AnsweringWithANoteSendsItAndClosesTheNote()
    {
        var decisions = bench.Decisions();
        decisions.Show(Bench.Of(Asking()));
        decisions.WriteNoteCommand.Execute(null);
        decisions.Note = "Pin the version";

        await decisions.AnswerCommand.ExecuteAsync(null);

        Assert.Equal(Option<string>.Some("Pin the version"), Assert.Single(bench.Permissions.Replies).Reply.Message);
        Assert.Equal((string.Empty, false), (decisions.Note, decisions.IsWritingNote));
    }

    [Fact]
    public void OpeningADecisionsConversationSelectsItsJobAndClosesThePopover()
    {
        var decisions = bench.Decisions();
        var asking = Asking();
        decisions.Show(Bench.Of(asking, Questioning()));
        var selected = new List<JobId>();
        bench.Messenger.Register<Contracts.Presentation.JobSelected>(this, (_, message) => selected.Add(message.Job));
        var closed = 0;
        decisions.CloseRequested += (_, _) => closed++;

        decisions.Items[0].OpenCommand.Execute(null);

        Assert.Equal([asking.Job], selected);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void EscapeAsksToCloseThePopover()
    {
        var decisions = bench.Decisions();
        var closed = 0;
        decisions.CloseRequested += (_, _) => closed++;

        decisions.CloseCommand.Execute(null);

        Assert.Equal(1, closed);
    }

    [Fact]
    public void TheHintsNameTheKeysThatAnswerTheSelectedDecision()
    {
        var decisions = bench.Decisions();
        decisions.Show(Bench.Of(Asking(), Questioning()));
        var onPermission = decisions.Hints.Select(hint => $"{hint.Keys} {hint.Action}").ToList();

        decisions.MoveNextCommand.Execute(null);

        Assert.Equal(["J K move", "⏎ allow", "⌫ deny", "⇧⏎ with a note"], onPermission);
        Assert.Equal(["J K move", "1–2 choose", "⏎ answer", "⌫ deny", "⇧⏎ with a note"], decisions.Hints.Select(hint => $"{hint.Keys} {hint.Action}"));
        Assert.Equal("esc close", $"{decisions.CloseHint.Keys} {decisions.CloseHint.Action}");
    }

    [Fact]
    public void ADecisionArrivingWhileThePopoverIsOpenKeepsTheSelectionOnItsDecision()
    {
        var decisions = bench.Decisions();
        var questioning = Questioning();
        decisions.Show(Bench.Of(questioning));
        var selected = decisions.Selected;

        decisions.Show(Bench.Of(Asking(), questioning));

        Assert.Equal(["Fix flaky CheckoutForm test", "Add invoice PDF endpoint"], decisions.Items.Select(item => item.JobTitle));
        Assert.Same(selected, decisions.Selected);
        Assert.Equal([false, true], decisions.Items.Select(item => item.IsSelected));
    }

    public void Dispose() => bench.Dispose();

    private BoardJob Asking()
    {
        var turn = TurnId.New();
        var asked = bench.Time.GetUtcNow().AddMinutes(-5);

        return Bench.OnBoard(bench.Job("Fix flaky CheckoutForm test", JobStatus.Running)) with
        {
            Transcript = Transcript.Empty
                .Apply(new TurnStarted(permissionSession, turn), asked)
                .Apply(new PermissionRequested(permissionSession, turn, new ItemId("run"), "Run the CheckoutForm tests", ItemKind.Command, "npm test -- CheckoutForm.test.tsx"), asked)
                .Apply(new PolicyDecision(permissionSession, turn, new ItemId("run"), Option<JobId>.None, ItemKind.Command, "npm test -- CheckoutForm.test.tsx", PolicyAnswer.Ask, Option<PolicyRule>.None, DecisionDelivery.LeftToHuman, asked)),
        };
    }

    private BoardJob Questioning()
    {
        var turn = TurnId.New();
        var asked = bench.Time.GetUtcNow().AddSeconds(-20);
        var form = new AgentForm(
            FormPurpose.Question,
            "Where should the PDF live?",
            "The invoice service renders PDFs already.",
            [new FormField("route", "Endpoint", "Which route?", FieldKind.SingleChoice, [new FormOption("GET /invoices/{id}/pdf", "A resource of its own", Recommended: true), new FormOption("GET /invoices/{id}?format=pdf", "Content negotiation")])]);

        return Bench.OnBoard(bench.Job("Add invoice PDF endpoint", JobStatus.Running)) with
        {
            Transcript = Transcript.Empty
                .Apply(new TurnStarted(formSession, turn), asked)
                .Apply(new FormRequested(formSession, turn, new ItemId("question"), form), asked)
                .Apply(new FormDecision(formSession, turn, new ItemId("question"), Option<JobId>.None, form, Autonomy.Supervised, Option<FormAnswer>.None, [], DecisionDelivery.LeftToHuman, asked)),
        };
    }
}
