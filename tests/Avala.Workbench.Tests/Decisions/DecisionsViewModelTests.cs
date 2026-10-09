using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Cards;
using Avala.Workbench.Decisions;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Decisions;

public sealed class DecisionsViewModelTests : IDisposable
{
    private readonly Bench bench = new();
    private readonly SessionId permissionSession = SessionId.New();
    private readonly SessionId formSession = SessionId.New();

    [Fact]
    public void EveryWaitingDecisionAcrossJobsIsListedOldestFirstWithItsWaitAndNothingElse()
    {
        var decisions = bench.Decisions();
        var empty = (decisions.IsEmpty, decisions.Empty);

        decisions.Show(Bench.Of(Asking(), Questioning(), Bench.OnBoard(bench.Job("Update the dependency", JobStatus.Running))));

        Assert.Equal((true, "Nothing needs you"), empty);
        Assert.Equal(
            [("Run the migration", "waiting 5m", typeof(PermissionCardViewModel)), ("Choose a database", "waiting less than a minute", typeof(FormCardViewModel))],
            decisions.Items.Select(item => (item.JobTitle, item.Waiting, item.Card.GetType())));
        Assert.Equal((false, decisions.Items[0]), (decisions.IsEmpty, decisions.Selected));
    }

    [Fact]
    public async Task TheKeyboardMovesBetweenDecisionsChoosesAnOptionAndAnswers()
    {
        var decisions = bench.Decisions();
        decisions.Show(Bench.Of(Asking(), Questioning()));
        decisions.Note = "Only on staging";

        await decisions.AnswerCommand.ExecuteAsync(null);
        decisions.MoveNextCommand.Execute(null);
        var atLast = decisions.MoveNextCommand.CanExecute(null);
        decisions.ChooseCommand.Execute("2");
        await decisions.AnswerCommand.ExecuteAsync(null);

        var (session, reply) = Assert.Single(bench.Permissions.Replies);
        Assert.Equal((permissionSession, PermissionAnswer.Allow, Option<string>.Some("Only on staging")), (session, reply.Answer, reply.Message));
        Assert.False(atLast);
        var (answered, answer) = Assert.Single(bench.Agents.Answers);
        Assert.Equal((formSession, "SQLite"), (answered, string.Join(",", Assert.Single(answer.Fields).Chosen)));
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
    }

    public void Dispose() => bench.Dispose();

    private BoardJob Asking()
    {
        var turn = TurnId.New();
        var asked = bench.Time.GetUtcNow().AddMinutes(-5);

        return Bench.OnBoard(bench.Job("Run the migration", JobStatus.Running)) with
        {
            Transcript = Transcript.Empty
                .Apply(new TurnStarted(permissionSession, turn), asked)
                .Apply(new PermissionRequested(permissionSession, turn, new ItemId("migrate"), "Run a command", ItemKind.Command, "dotnet ef database update"), asked)
                .Apply(new PolicyDecision(permissionSession, turn, new ItemId("migrate"), Option<JobId>.None, ItemKind.Command, "dotnet ef database update", PolicyAnswer.Ask, Option<PolicyRule>.None, DecisionDelivery.LeftToHuman, asked)),
        };
    }

    private BoardJob Questioning()
    {
        var turn = TurnId.New();
        var asked = bench.Time.GetUtcNow().AddSeconds(-20);
        var form = new AgentForm(
            FormPurpose.Question,
            "Choose a database",
            "The service stores orders.",
            [new FormField("database", "Database", "Which one?", FieldKind.SingleChoice, [new FormOption("PostgreSQL", "Relational", Recommended: true), new FormOption("SQLite", "A file")])]);

        return Bench.OnBoard(bench.Job("Choose a database", JobStatus.Running)) with
        {
            Transcript = Transcript.Empty
                .Apply(new TurnStarted(formSession, turn), asked)
                .Apply(new FormRequested(formSession, turn, new ItemId("question"), form), asked)
                .Apply(new FormDecision(formSession, turn, new ItemId("question"), Option<JobId>.None, form, Autonomy.Supervised, Option<FormAnswer>.None, [], DecisionDelivery.LeftToHuman, asked)),
        };
    }
}
