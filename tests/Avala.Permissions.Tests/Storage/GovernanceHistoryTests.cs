using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Permissions.Governance;
using Avala.Permissions.Storage;
using Avala.Permissions.Tests.Governance;
using Avala.Sdk;
using Avala.Storage;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Permissions.Tests.Storage;

public sealed class GovernanceHistoryTests
{
    private static readonly DateTimeOffset Nine = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    private static readonly PolicyRule NoMigrations =
        new(RuleOrigin.Repository, "no-migrations", ItemKind.Command, "dotnet ef *", RuleScope.Anywhere, PolicyAnswer.Deny);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheGovernanceOfEarlierRunsIsRestoredForItsJobWhileALiveSessionKeepsItsOwnAsync()
    {
        var store = new InMemoryGovernance();
        var book = new GovernanceBook(store);
        var history = History(JobId.New(), SessionId.New());
        var job = Outcomes.Present(history.Decisions[0].Job);
        var live = history.Policies[0].Session;
        book.Keep(book.Of(live).WorkingOn(job));
        var earlier = SessionId.New();
        store.Earlier = History(job, earlier) with { Answers = history.Answers };

        await book.RunAsync(Cancellation);

        Assert.Equal(store.Earlier.Decisions, book.OfJob(job));
        Assert.Equal(store.Earlier.Forms, book.FormsOfJob(job));
        Assert.Equal(store.Earlier.Answers, book.AnswersOfJob(job));
        Assert.Equal(Option<SessionAutonomy>.Some(store.Earlier.Autonomies[0]), book.AutonomyOf(earlier));
        Assert.Equal(Option<SessionPolicy>.Some(store.Earlier.Policies[0]), book.PolicyOf(earlier));
        Assert.Empty(book.OfSession(live));
    }

    [Fact]
    public async Task GovernanceSurvivesAReopeningAndEarlierRunsLeaveOutWhatThisRunStoredAsync()
    {
        await using var folder = new TemporaryFolder();
        var earlier = History(JobId.New(), SessionId.New());
        await using (var first = new SqliteGovernanceStore(new AvalaPaths(folder.Path)))
        {
            await first.RecordAsync(earlier.Policies[0], Cancellation);
            await first.RecordAsync(earlier.Autonomies[0], Cancellation);
            await first.RecordAsync(earlier.Decisions[0], Cancellation);
            await first.RecordAsync(earlier.Forms[0], Cancellation);
            await first.RecordAsync(earlier.Answers[0], Cancellation);
        }

        await using var second = new SqliteGovernanceStore(new AvalaPaths(folder.Path));
        await second.RecordAsync(History(JobId.New(), SessionId.New()).Decisions[0], Cancellation);

        Assert.Equal(StoredJson.Write(earlier), StoredJson.Write(await second.EarlierRunsAsync(Cancellation)));
    }

    private static GovernanceHistory History(JobId job, SessionId session)
    {
        var turn = TurnId.New();
        var form = new AgentForm(
            FormPurpose.Question,
            "Choose a database",
            "The service needs to store its orders.",
            [new FormField("database", "Database", "Which database?", FieldKind.SingleChoice, [new FormOption("PostgreSQL", "Relational.", Recommended: true), new FormOption("SQLite", "A file.")])]);

        return new GovernanceHistory(
            [new SessionPolicy(session, PolicyFileStatus.Applied, Option<PolicyError>.None, [NoMigrations], new FileOrigin("4f2a9c1", EditedInWorktree: false)) { Autonomy = Autonomy.Autonomous, Strategy = FormStrategy.Recommended }],
            [new SessionAutonomy(session, job, Autonomy.Autonomous, Autonomy.Supervised, Autonomy.Supervised, Refused: false)],
            [new PolicyDecision(session, turn, new ItemId("migrate"), job, ItemKind.Command, "dotnet ef database update", PolicyAnswer.Deny, NoMigrations, DecisionDelivery.Answered, Nine) { Autonomy = Autonomy.Supervised }],
            [new FormDecision(session, turn, new ItemId("question"), job, form, Autonomy.Autonomous, new FormAnswer(new ItemId("question"), [new FieldAnswer("database") { Chosen = ["PostgreSQL"] }]), [new Assumption("database", "Which database?", AssumptionBasis.RecommendedOption, ["PostgreSQL"])], DecisionDelivery.Answered, Nine.AddMinutes(1))],
            [new HumanAnswer(session, job, new ItemId("build"), ItemKind.Command, "dotnet build", PermissionAnswer.Allow, "Go ahead.", Option<PolicyRule>.None, Nine.AddMinutes(2))]);
    }
}
