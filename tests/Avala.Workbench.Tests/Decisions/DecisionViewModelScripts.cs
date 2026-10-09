using Avala.Agents.Contracts.Events;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Cards;
using Avala.Workbench.Conversation;
using Avala.Workbench.Decisions;
using Avala.Workbench.Tests.Cards;

namespace Avala.Workbench.Tests.Decisions;

public sealed class DecisionViewModelScripts
{
    private static readonly DateTimeOffset Since = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);
    private readonly Asking asking = new();
    private readonly FakePermissionAnswers permissions = new();
    private readonly FakeAgents agents = new();

    [Theory]
    [InlineData(20, "<1m")]
    [InlineData(300, "5m")]
    [InlineData(4500, "1h 15m")]
    public void ADecisionTellsHowLongItHasWaited(int seconds, string waiting) =>
        ViewModelScript.Given(Decision())
            .When(decision => decision.Update(asking.Permission(), Since.AddSeconds(seconds)))
            .ThenNotified(nameof(DecisionViewModel.Waiting))
            .Then(decision => Assert.Equal(waiting, decision.Waiting));

    [Fact]
    public void APermissionNamesItsRequestWhatTheAgentWantsAndTheWholeTarget() =>
        ViewModelScript.Given(Decision())
            .Then(decision => Assert.Equal(
                ("Fix flaky CheckoutForm test", "Run the CheckoutForm tests", "wants to run a command", "npm test -- CheckoutForm.test.tsx --runInBand", true, 0),
                (decision.JobTitle, decision.Title, decision.Asking, decision.Target, decision.IsPermission, decision.Options.Count)));

    [Fact]
    public void AFormNamesItsQuestionItsContextAndNumbersTheOptionsOfItsFirstChoice() =>
        ViewModelScript.Given(Decision(new FormCardViewModel(asking.Form(), Replies())))
            .Then(decision =>
            {
                Assert.Equal(("Add invoice PDF endpoint", "asks a question", "The service stores invoices.", false, true), (decision.Title, decision.Asking, decision.Context, decision.IsPermission, decision.IsSingleChoice));
                Assert.Equal([(1, "PostgreSQL"), (2, "SQLite")], decision.Options.Select(option => (option.Number, option.Choice.Label)));
            });

    [Fact]
    public void AnUpdateReachesItsCard() =>
        ViewModelScript.Given(Decision())
            .When(decision => decision.Update(asking.Permission() with { Resolution = PermissionAnswer.Deny }, Since))
            .Then(decision => Assert.Equal("Denied", Assert.IsType<PermissionCardViewModel>(decision.Card).Verdict));

    [Fact]
    public void ADecisionAnsweredElsewhereCanNoLongerBeAnswered() =>
        ViewModelScript.Given(Decision())
            .When(decision => decision.Update(asking.Permission() with { Resolution = PermissionAnswer.Allow }, Since))
            .Then(decision => Assert.Equal((false, false), (decision.AnswerCommand.CanExecute(null), decision.DenyCommand.CanExecute(null))));

    [Fact]
    public async Task DenyingSendsTheNoteTheParentHandedDown()
    {
        var decision = Decision();
        decision.Note = "Not on CI";

        await decision.DenyCommand.ExecuteAsync(null);

        var (_, reply) = Assert.Single(permissions.Replies);
        Assert.Equal((PermissionAnswer.Deny, "Not on CI"), (reply.Answer, reply.Message.Match(message => message, () => string.Empty)));
    }

    [Fact]
    public void ADecisionWhoseWaitHasNoStartShowsNoWaitRatherThanAnAbsurdOne() =>
        ViewModelScript.Given(new DecisionViewModel(Jobs.Contracts.JobId.New(), "Fix flaky CheckoutForm test", new PermissionCardViewModel(asking.Permission(), Replies()), Option<DateTimeOffset>.None))
            .When(decision => decision.Update(asking.Permission(), Since.AddHours(3)))
            .Then(decision => Assert.Equal(string.Empty, decision.Waiting));

    [Fact]
    public async Task DontAskAgainChosenInThePopoverReachesTheAnswerAndSaysItLastsThisSession()
    {
        var decision = Decision();

        decision.DontAskAgain = true;
        await decision.AnswerCommand.ExecuteAsync(null);

        var (_, reply) = Assert.Single(permissions.Replies);
        Assert.Equal((PermissionAnswer.Allow, true), (reply.Answer, reply.DontAskAgain));
        Assert.Equal("Don't ask again this session", decision.DontAskAgainLabel);
        Assert.StartsWith("Your answer is reused only for this exact command and only until this agent session ends", decision.DontAskAgainScope, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFormWithFreeTextAConfirmationAndSeveralFieldsIsAnsweredFromThePopoverThroughItsFields()
    {
        var decision = Decision(new FormCardViewModel(
            asking.Form(
                new FormField("tag", "Tag", "Which tag?", FieldKind.FreeText, []),
                new FormField("targets", "Targets", "Where?", FieldKind.MultipleChoice, [new FormOption("NuGet", "Feed", Recommended: true), new FormOption("GitHub", "Page")]),
                new FormField("notes", "Notes", "Publish the notes?", FieldKind.Confirmation, [])),
            Replies()));
        var blank = (decision.HasFields, decision.Options.Count, decision.AnswerCommand.CanExecute(null));

        decision.Fields[0].Text = "v1.4.0";
        decision.Fields[2].Confirmed = true;
        await decision.AnswerCommand.ExecuteAsync(null);

        Assert.Equal((true, 0, false), blank);
        var answer = Assert.Single(agents.Answers).Answer;
        Assert.Equal(
            [("tag", "", "v1.4.0", false), ("targets", "NuGet", "", false), ("notes", "", "", true)],
            answer.Fields.Select(field => (field.Field, string.Join(",", field.Chosen), field.Text.Match(text => text, () => string.Empty), field.Confirmed)));
    }

    [Fact]
    public void ASingleChoiceThatAcceptsTextIsAnsweredThroughItsFieldSoTheTextCanBeTyped() =>
        ViewModelScript.Given(Decision(new FormCardViewModel(
                asking.Form(new FormField("database", "Database", "Which one?", FieldKind.SingleChoice, [new FormOption("PostgreSQL", "Relational", Recommended: true)], AcceptsFreeText: true)),
                Replies())))
            .Then(decision => Assert.Equal((true, 0), (decision.HasFields, decision.Options.Count)));

    [Fact]
    public void OpeningTheConversationIsReportedToTheList()
    {
        var decision = Decision();
        var opened = 0;
        decision.Opened += (_, _) => opened++;

        decision.OpenCommand.Execute(null);

        Assert.Equal(1, opened);
    }

    private Workbench.Replies.HumanReplies Replies() => new(permissions, agents);

    private DecisionViewModel Decision() => Decision(new PermissionCardViewModel(asking.Permission(), Replies()));

    private static DecisionViewModel Decision(ITimelineItem card) => new(Jobs.Contracts.JobId.New(), "Fix flaky CheckoutForm test", card, Since);
}
