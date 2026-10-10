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
    public Task EveryWaitingDecisionIsListedWithTheSelectedOneOpenAndTheKeysThatAnswerItAsync() =>
        ui.RunAsync(() =>
        {
            var view = Tall(Screen.Show(new DesignDecisionsViewModel()));

            Assert.Equal((3, "3 pending"), (view.Find<ListBox>("Items").ItemCount, view.TextOf("Pending")));
            Assert.False(view.Shows("EmptyState"));
            Assert.Superset(
                new HashSet<string>(["Where should generated PDFs live?", "6m", "Render on each request", "Recommended", "Answer  ⏎", "J K", "move", "1–3", "choose", "esc", "close"]),
                view.VisibleTexts.ToHashSet());
            Assert.Single(view.All<StackPanel>(), panel => panel.Name == "Details");
        }, Cancellation);

    [Fact]
    public Task ASubAgentsDecisionShowsItsParentsHeadingAndThatItWaitsForItsParentAsync() =>
        ui.RunAsync(() =>
        {
            var child = Workbench.Decisions.DesignDecisionViewModel.Permission(JobId.New(), "Migrate the database", "Run dotnet ef database update", "dotnet ef database update", "1m", heading: "Sub-agents of Ship the release", status: "Waiting for its parent");
            var root = Workbench.Decisions.DesignDecisionViewModel.Permission(JobId.New(), "Ship the release", "Run dotnet test", "dotnet test", "2m");

            var shown = Screen.Show(child);
            var plain = Screen.Show(root);

            Assert.Equal(("Sub-agents of Ship the release", "Waiting for its parent"), (shown.TextOf("Heading"), shown.TextOf("Status")));
            Assert.Equal((false, "Waiting for you"), (plain.Shows("Heading"), plain.TextOf("Status")));
        }, Cancellation);

    [Fact]
    public Task ALongCommandShowsWholeInABoundedScrollingWellWithWhatItWritesAboveAllowAsync() =>
        ui.RunAsync(() =>
        {
            var command = string.Join('\n', Enumerable.Range(1, 60).Select(line => $"echo {line} >> /tmp/out.txt"));
            var decision = Workbench.Decisions.DesignDecisionViewModel.Permission(Jobs.Contracts.JobId.New(), "Build a timer", "Run 60 commands: echo > /tmp/out.txt", command, "1m", isSelected: true, writes: "Writes to /tmp/out.txt");
            var view = Tall(Screen.Show(decision));
            var scroll = view.Find<ScrollViewer>("TargetScroll");

            Assert.Equal((command, "Writes to /tmp/out.txt"), (view.Find<SelectableTextBlock>("Target").Text, view.TextOf("Writes")));
            Assert.True(scroll.Bounds.Height <= 160 && scroll.Extent.Height > scroll.Viewport.Height, $"the well is {scroll.Bounds.Height} high for {scroll.Extent.Height}");
            Assert.True(view.Shows("Answer"));
        }, Cancellation);

    [Fact]
    public Task WithNothingWaitingThePopoverSaysSoAndOffersNoAnswerAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var view = Screen.Show(bench.Decisions());

            Assert.Equal((true, false), (view.Shows("EmptyState"), view.Shows("Items")));
            Assert.Equal(("Nothing needs you", "all answered"), (view.TextOf("Empty"), view.TextOf("Pending")));
            Assert.Contains("Every agent is working again.", view.VisibleTexts);
        }, Cancellation);

    [Fact]
    public Task TheKeyboardAloneMovesWithJAndKChoosesTheSecondOptionAndAnswersAsync() =>
        ui.RunAsync(async () =>
        {
            using var bench = new Bench();
            var decisions = Waiting(bench);
            var view = Tall(Screen.Show(decisions));
            var focusedOnShow = view.Find<ListBox>("Items").ContainerFromIndex(0)!.IsFocused;

            view.Press(Key.J);
            view.Press(Key.K);
            view.Press(Key.J);
            view.Press(Key.D2);
            view.Press(Key.Enter);
            await (decisions.AnswerCommand.ExecutionTask ?? Task.CompletedTask);

            Assert.True(focusedOnShow);
            Assert.Equal(["GET /invoices/{id}?format=pdf"], Assert.Single(Assert.Single(bench.Agents.Answers).Answer.Fields).Chosen);
            Assert.Empty(bench.Permissions.Replies);
        }, Cancellation);

    [Fact]
    public Task BackspaceDeniesTheSelectedPermissionAsync() =>
        ui.RunAsync(async () =>
        {
            using var bench = new Bench();
            var decisions = Waiting(bench);
            var view = Tall(Screen.Show(decisions));

            view.Press(Key.Back);
            await (decisions.DenyCommand.ExecutionTask ?? Task.CompletedTask);

            Assert.Equal(PermissionAnswer.Deny, Assert.Single(bench.Permissions.Replies).Reply.Answer);
        }, Cancellation);

    [Fact]
    public Task ShiftEnterOpensTheNoteWhereDigitsAndBackspaceTypeAndEnterAnswersWithItAsync() =>
        ui.RunAsync(async () =>
        {
            using var bench = new Bench();
            var decisions = Waiting(bench);
            var view = Tall(Screen.Show(decisions));
            var closed = view.Shows("NoteBox");

            view.Press(Key.Enter, RawInputModifiers.Shift);
            var focused = view.Find<TextBox>("DecisionNote").IsFocused;
            view.Type("DecisionNote", "Only 22 tests");
            view.Press(Key.D2);
            view.Press(Key.Back);
            view.Press(Key.Enter);
            await (decisions.AnswerCommand.ExecutionTask ?? Task.CompletedTask);

            Assert.Equal((false, true), (closed, focused));
            var (_, reply) = Assert.Single(bench.Permissions.Replies);
            Assert.Equal((PermissionAnswer.Allow, Option<string>.Some("Only 22 test")), (reply.Answer, reply.Message));
        }, Cancellation);

    [Fact]
    public Task TypingADigitInTheNoteWritesItInsteadOfChoosingAnOptionAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var decisions = Waiting(bench);
            decisions.MoveNextCommand.Execute(null);
            var view = Tall(Screen.Show(decisions));
            view.Press(Key.Enter, RawInputModifiers.Shift);

            view.Type("DecisionNote", "2");
            view.Press(Key.D2);

            var card = Assert.IsType<FormCardViewModel>(decisions.Items[1].Card);
            Assert.Equal([true, false], card.Fields[0].Choices.Select(choice => choice.IsSelected));
            Assert.Equal("2", decisions.Note);
        }, Cancellation);

    [Fact]
    public Task DeletingAWordOfTheNoteDoesNotDenyTheDecisionAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var decisions = Waiting(bench);
            var view = Tall(Screen.Show(decisions));
            view.Press(Key.Enter, RawInputModifiers.Shift);
            view.Type("DecisionNote", "Only the checkout tests");

            view.Press(Key.Back, RawInputModifiers.Control);
            view.Press(Key.Back);

            Assert.Empty(bench.Permissions.Replies);
            Assert.Equal("Only the checkout", decisions.Note);
        }, Cancellation);

    [Fact]
    public Task EscapeAsksToCloseThePopoverAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var decisions = Waiting(bench);
            var closed = 0;
            decisions.CloseRequested += (_, _) => closed++;
            var view = Tall(Screen.Show(decisions));

            view.Press(Key.Escape);

            Assert.Equal(1, closed);
        }, Cancellation);

    [Fact]
    public Task DecisionsArrivingAndLeavingWhileOpenKeepTheSelectionOnItsDecisionAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var decisions = bench.Decisions();
            var asking = Asking(bench);
            var questioning = Questioning(bench);
            decisions.Show(Bench.Of(questioning));
            var view = Tall(Screen.Show(decisions));
            var selected = decisions.Selected;

            decisions.Show(Bench.Of(asking, questioning));
            view.Settle();
            var afterArrival = (decisions.Selected, view.Find<ListBox>("Items").SelectedItem, view.Find<ListBox>("Items").ItemCount);
            decisions.Show(Bench.Of(asking with { Transcript = Transcript.Empty }, questioning));
            view.Settle();

            Assert.Equal((selected, (object?)selected, 2), afterArrival);
            Assert.Equal((selected, (object?)selected, 1), (decisions.Selected, view.Find<ListBox>("Items").SelectedItem, view.Find<ListBox>("Items").ItemCount));
            Assert.True(view.Shows("Details"));
        }, Cancellation);

    [Fact]
    public Task TheSelectedDecisionLeavingMovesTheSelectionAndTheKeyboardFocusToTheNextAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var decisions = bench.Decisions();
            var asking = Asking(bench);
            var questioning = Questioning(bench);
            decisions.Show(Bench.Of(asking, questioning));
            var view = Tall(Screen.Show(decisions));

            decisions.Show(Bench.Of(asking with { Transcript = Transcript.Empty }, questioning));
            view.Settle();

            Assert.Same(decisions.Items[0], decisions.Selected);
            Assert.True(view.Find<ListBox>("Items").ContainerFromIndex(0)!.IsKeyboardFocusWithin);
        }, Cancellation);

    [Fact]
    public Task ClickingAnOptionChoosesItAndClickingAllowAnswersAsync() =>
        ui.RunAsync(async () =>
        {
            using var bench = new Bench();
            var decisions = Waiting(bench);
            var view = Tall(Screen.Show(decisions));

            view.Click("Answer");
            await (decisions.Items[0].AnswerCommand.ExecutionTask ?? Task.CompletedTask);
            decisions.MoveNextCommand.Execute(null);
            view.Settle();
            view.Click(view.All<RadioButton>()[1]);

            var card = Assert.IsType<FormCardViewModel>(decisions.Items[1].Card);
            Assert.Equal(PermissionAnswer.Allow, Assert.Single(bench.Permissions.Replies).Reply.Answer);
            Assert.Equal([false, true], card.Fields[0].Choices.Select(choice => choice.IsSelected));
        }, Cancellation);

    [Fact]
    public Task ALongCommandLineWrapsInsideThePopoverAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var decisions = bench.Decisions();
            var command = $"pnpm vitest run src/checkout/CheckoutForm.test.tsx --reporter=verbose {string.Join(' ', Enumerable.Repeat("--testNamePattern=submits", 12))}";
            decisions.Show(Bench.Of(Asking(bench, command)));
            var view = Tall(Screen.Show(decisions));

            var target = view.Find<SelectableTextBlock>("Target");
            Assert.Equal(command, target.Text);
            Assert.True(target.Bounds.Width <= view.Find<Border>("Surface").Bounds.Width);
            Assert.True(target.Bounds.Height > 40);
        }, Cancellation);

    [Fact]
    public Task AFormWithFieldsIsFilledBelowTheListWhereTypedKeysNeverMoveTheSelectionAndEnterAnswersAsync() =>
        ui.RunAsync(async () =>
        {
            using var bench = new Bench();
            var decisions = bench.Decisions();
            decisions.Show(Bench.Of(Releasing(bench)));
            var view = Tall(Screen.Show(decisions));
            var shown = (view.Shows("FormBox"), view.Shows("FieldsBelow"), view.Shows("Options"));

            view.Type("Text", "jk 1.4");
            view.Click(view.All<CheckBox>().Single(box => box.Name == "Confirmed"));
            view.Find("Text").Focus();
            view.Press(Key.Enter);
            await (decisions.Items[0].AnswerCommand.ExecutionTask ?? Task.CompletedTask);

            Assert.Equal((true, true, false), shown);
            var answer = Assert.Single(bench.Agents.Answers).Answer;
            Assert.Equal([("tag", "jk 1.4", false), ("notes", string.Empty, true)], answer.Fields.Select(field => (field.Field, field.Text.Match(text => text, () => string.Empty), field.Confirmed)));
        }, Cancellation);

    [Fact]
    public Task DontAskAgainForThisJobIsOfferedOnAPermissionAndSentWithTheAnswerAsync() =>
        ui.RunAsync(async () =>
        {
            using var bench = new Bench();
            var decisions = bench.Decisions();
            decisions.Show(Bench.Of(Asking(bench)));
            var view = Tall(Screen.Show(decisions));

            view.Click("DontAskAgain");
            view.Click("Answer");
            await (decisions.Items[0].AnswerCommand.ExecutionTask ?? Task.CompletedTask);

            Assert.Equal("Don't ask again for this job", view.Find<CheckBox>("DontAskAgain").Content);
            Assert.False(view.Shows("AlwaysInRepository"));
            Assert.Equal(Remember.ForThisJob, Assert.Single(bench.Permissions.Replies).Reply.Remember);
        }, Cancellation);

    [Fact]
    public Task AlwaysInThisRepositoryIsOfferedNextToItWhenThePolicyFoundAnExactRuleAndSentWithTheAnswerAsync() =>
        ui.RunAsync(async () =>
        {
            using var bench = new Bench();
            var decisions = bench.Decisions();
            decisions.Show(Bench.Of(Asking(bench, "dotnet test", inRepository: true)));
            var view = Tall(Screen.Show(decisions));

            view.Click("AlwaysInRepository");
            view.Click("Answer");
            await (decisions.Items[0].AnswerCommand.ExecutionTask ?? Task.CompletedTask);

            Assert.Equal("Always in this repository", view.Find<CheckBox>("AlwaysInRepository").Content);
            Assert.True(view.Find<CheckBox>("DontAskAgain").IsChecked);
            Assert.Equal(Remember.InThisRepository, Assert.Single(bench.Permissions.Replies).Reply.Remember);
        }, Cancellation);

    private static BoardJob Releasing(Bench bench)
    {
        var session = SessionId.New();
        var turn = TurnId.New();
        var asked = bench.Time.GetUtcNow();
        var form = new AgentForm(
            FormPurpose.Other,
            "Prepare the release",
            string.Empty,
            [new FormField("tag", "Tag", "Which tag?", FieldKind.FreeText, []), new FormField("notes", "Notes", "Publish the notes?", FieldKind.Confirmation, [])]);

        return Bench.OnBoard(bench.Job("Ship 1.4", JobStatus.Running)) with
        {
            Transcript = Transcript.Empty
                .Apply(new TurnStarted(session, turn), asked)
                .Apply(new FormRequested(session, turn, new ItemId("release"), form), asked)
                .Apply(new FormDecision(session, turn, new ItemId("release"), Option<JobId>.None, form, Autonomy.Supervised, Option<FormAnswer>.None, [], DecisionDelivery.LeftToHuman, asked)),
        };
    }

    private static ViewScript Tall(ViewScript view)
    {
        view.Window.Height = 900;

        return view.Settle();
    }

    private static DecisionsViewModel Waiting(Bench bench)
    {
        var decisions = bench.Decisions();
        decisions.Show(Bench.Of(Asking(bench), Questioning(bench)));

        return decisions;
    }

    private static BoardJob Asking(Bench bench, string target = "npm test", bool inRepository = false)
    {
        var offered = inRepository
            ? new PolicyRule(RuleOrigin.Repository, "always in this repository", ItemKind.Command, target, RuleScope.Anywhere, PolicyAnswer.Allow)
            : Option<PolicyRule>.None;
        var session = SessionId.New();
        var turn = TurnId.New();
        var asked = bench.Time.GetUtcNow().AddMinutes(-4);

        return Bench.OnBoard(bench.Job("Fix flaky CheckoutForm test", JobStatus.Running)) with
        {
            Transcript = Transcript.Empty
                .Apply(new TurnStarted(session, turn), asked)
                .Apply(new PermissionRequested(session, turn, new ItemId("run"), "Run the CheckoutForm tests", ItemKind.Command, target), asked)
                .Apply(new PolicyDecision(session, turn, new ItemId("run"), Option<JobId>.None, ItemKind.Command, target, PolicyAnswer.Ask, Option<PolicyRule>.None, DecisionDelivery.LeftToHuman, asked) { RepositoryRule = offered }),
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
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheOpenDecisionShowsItsQuestionContextAndNumberedOptionsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignDecisionViewModel());

            Assert.Equal(("Where should generated PDFs live?", "Add invoice PDF endpoint · asks a question", "6m"), (view.TextOf("Title"), view.TextOf("Asked"), view.TextOf("Waiting")));
            Assert.Equal((true, false, false), (view.Shows("Options"), view.Shows("TargetWell"), view.Shows("Deny")));
            Assert.Superset(new HashSet<string>(["1", "2", "3", "Render on each request", "Recommended", "Answer  ⏎"]), view.VisibleTexts.ToHashSet());
        }, Cancellation);

    [Fact]
    public Task AFoldedPermissionShowsOnlyItsRowAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(DesignDecisionViewModel.Permission(JobId.New(), "Fix flaky CheckoutForm test", "Run pnpm add -D @testing-library/user-event", "pnpm add -D @testing-library/user-event@14.5.2", "2m"));

            Assert.Equal(("Fix flaky CheckoutForm test · wants to run a command", false), (view.TextOf("Asked"), view.Shows("Details")));
        }, Cancellation);

    [Fact]
    public Task AnOpenPermissionShowsItsWholeCommandAndAllowAndDenyAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(DesignDecisionViewModel.Permission(JobId.New(), "Fix flaky CheckoutForm test", "Run pnpm add -D @testing-library/user-event", "pnpm add -D @testing-library/user-event@14.5.2", "2m", isSelected: true));

            Assert.Equal("pnpm add -D @testing-library/user-event@14.5.2", view.Find<SelectableTextBlock>("Target").Text);
            Assert.Equal((true, false), (view.Shows("Deny"), view.Shows("Options")));
            Assert.Contains("Allow  ⏎", view.VisibleTexts);
            Assert.Equal("JetBrains Mono", view.Find<SelectableTextBlock>("Target").FontFamily.FamilyNames[0]);
        }, Cancellation);
}
