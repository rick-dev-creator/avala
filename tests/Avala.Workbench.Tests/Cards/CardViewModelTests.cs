using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Workbench.Cards;
using Avala.Workbench.Replies;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Cards;

public sealed class CardViewModelTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private readonly SessionId session = SessionId.New();
    private readonly TurnId turn = TurnId.New();
    private readonly FakePermissionAnswers permissions = new();
    private readonly FakeAgents agents = new();

    [Fact]
    public async Task AllowingSendsTheNoteAndDontAskAgainToTheSessionOfTheRequest()
    {
        var card = new PermissionCardViewModel(Asked(), Replies()) { Note = " Only on staging ", DontAskAgain = true };

        await card.AllowCommand.ExecuteAsync(null);

        var (answered, reply) = Assert.Single(permissions.Replies);
        Assert.Equal((session, new ItemId("migrate"), PermissionAnswer.Allow, Option<string>.Some("Only on staging"), true), (answered, reply.Item, reply.Answer, reply.Message, reply.DontAskAgain));
    }

    [Fact]
    public async Task AnAnswerTheAgentNoLongerWaitsForIsReportedOnTheCard()
    {
        permissions.Refuse = true;
        var card = new PermissionCardViewModel(Asked(), Replies());

        await card.DenyCommand.ExecuteAsync(null);

        Assert.Equal("The agent no longer waits for this answer.", card.Error);
    }

    [Fact]
    public void ACardIsAnsweredOnlyWhileItWaitsForAPerson()
    {
        var card = new PermissionCardViewModel(Asked(), Replies());

        card.Update(Asked() with { Resolution = PermissionAnswer.Allow });

        Assert.Equal((false, "Allowed"), (card.AllowCommand.CanExecute(null), card.Verdict));
    }

    [Fact]
    public async Task AFormIsAnsweredWithItsRecommendedOptionsByDefault()
    {
        var card = new FormCardViewModel(Form(), Replies());

        await card.SubmitCommand.ExecuteAsync(null);

        var (answered, answer) = Assert.Single(agents.Answers);
        Assert.Equal(session, answered);
        Assert.Equal(["PostgreSQL"], Assert.Single(answer.Fields).Chosen);
    }

    [Fact]
    public void ChoosingAnotherOptionOfASingleChoiceUnselectsTheFirst()
    {
        var card = new FormCardViewModel(Form(), Replies());
        var field = Assert.Single(card.Fields);

        field.Choices[1].IsSelected = true;

        Assert.Equal(["SQLite"], field.Choice().Chosen);
    }

    [Fact]
    public async Task DecliningAFormTellsTheAgentWhy()
    {
        var card = new FormCardViewModel(Form(), Replies()) { Note = "Ask the team first" };

        await card.DeclineCommand.ExecuteAsync(null);

        var answer = Assert.Single(agents.Answers).Answer;
        Assert.Equal((true, Option<string>.Some("Ask the team first")), (answer.Declined, answer.Message));
    }

    private HumanReplies Replies() => new(permissions, agents);

    private PermissionEntry Asked() =>
        Assert.IsType<PermissionEntry>(Assert.Single(Transcript.Empty
            .Apply(new TurnStarted(session, turn), Now)
            .Apply(new PermissionRequested(session, turn, new ItemId("migrate"), "Run a command", ItemKind.Command, "dotnet ef database update"), Now)
            .Apply(new PolicyDecision(session, turn, new ItemId("migrate"), Option<JobId>.None, ItemKind.Command, "dotnet ef database update", PolicyAnswer.Ask, Option<PolicyRule>.None, DecisionDelivery.LeftToHuman, Now))
            .Entries));

    private FormEntry Form()
    {
        var form = new AgentForm(
            FormPurpose.Question,
            "Choose a database",
            "The service stores orders.",
            [new FormField("database", "Database", "Which one?", FieldKind.SingleChoice, [new FormOption("PostgreSQL", "Relational", Recommended: true), new FormOption("SQLite", "A file")])]);

        return Assert.IsType<FormEntry>(Assert.Single(Transcript.Empty
            .Apply(new TurnStarted(session, turn), Now)
            .Apply(new FormRequested(session, turn, new ItemId("question"), form), Now)
            .Apply(new FormDecision(session, turn, new ItemId("question"), Option<JobId>.None, form, Autonomy.Supervised, Option<FormAnswer>.None, [], DecisionDelivery.LeftToHuman, Now))
            .Entries));
    }
}
