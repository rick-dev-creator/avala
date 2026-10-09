using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class HumanInputTests(PublishedPlugins plugins)
{
    private const string Autonomous = """{ "autonomy": "autonomous" }""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AQuestionWaitsForAHumanWhoAnswersItThroughTheAgentsAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "question");

        var asked = await run.FormDecisionAsync();
        var field = Assert.Single(asked.Form.Fields);
        Outcomes.Succeeds(await run.Get<IAgents>().AnswerAsync(
            asked.Session,
            new FormAnswer(asked.Item, [new FieldAnswer(field.Id) { Chosen = ["SQLite"] }]),
            Cancellation));

        Assert.Equal((DecisionDelivery.LeftToHuman, Autonomy.Supervised, true), (asked.Delivery, asked.Autonomy, asked.Answer.IsNone));
        Assert.Contains(await run.TurnAsync(), update => update is ItemProgressed { Text: "Going with Database: SQLite" });
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        Assert.Equal([asked], run.Get<IPermissionAudit>().FormsOfJob(run.Job));
    }

    [Fact]
    public async Task AnAutonomousJobHasItsQuestionAnsweredWithTheRecommendedOptionAndTheAssumptionAuditedAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "question", (".avala/permissions.json", Autonomous));

        var decided = await run.FormDecisionAsync();

        Assert.Equal((DecisionDelivery.Answered, Autonomy.Autonomous), (decided.Delivery, decided.Autonomy));
        var assumption = Assert.Single(decided.Assumptions);
        Assert.Equal(("database", AssumptionBasis.RecommendedOption, "PostgreSQL"), (assumption.Field, assumption.Basis, Assert.Single(assumption.Chosen)));
        Assert.Contains(await run.TurnAsync(), update => update is ItemProgressed { Text: "Going with Database: PostgreSQL" });
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        Assert.Equal([decided], run.Get<IPermissionAudit>().FormsOfJob(run.Job));
    }

    [Fact]
    public async Task APermissionDeniedWithAMessageTellsTheAgentWhyAsync()
    {
        const string Why = "Use the staging database instead.";
        await using var run = await SimulatedRun.StartAsync(plugins, "permission");
        var asked = await run.DecisionAsync();

        var answer = Outcomes.Succeeds(await run.Get<IPermissionAnswers>().AnswerAsync(
            asked.Session,
            new PermissionReply(asked.Item, PermissionAnswer.Deny) { Message = Why },
            Cancellation));

        var turn = await run.TurnAsync();
        Assert.Contains(turn, update => update is ItemCompleted { Item.Value: "migrate", Outcome: ItemOutcome.Cancelled });
        Assert.Contains(turn, update => update is ItemProgressed { Text: $"Understood, I will not go on: {Why}" });
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        Assert.Equal([answer], run.Get<IPermissionAudit>().AnswersOfJob(run.Job));
    }

    [Fact]
    public async Task DontAskAgainBecomesASessionRuleThatAnswersTheNextIdenticalRequestAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "repeated-permission");
        var first = await run.DecisionAsync();

        var answer = Outcomes.Succeeds(await run.Get<IPermissionAnswers>().AnswerAsync(
            first.Session,
            new PermissionReply(first.Item, PermissionAnswer.Allow) { DontAskAgain = true },
            Cancellation));
        var second = await run.DecisionAsync();

        Assert.Equal(DecisionDelivery.LeftToHuman, first.Delivery);
        var rule = Outcomes.Present(answer.SessionRule);
        Assert.Equal((PolicyAnswer.Allow, DecisionDelivery.Answered, Option<PolicyRule>.Some(rule)), (second.Answer, second.Delivery, second.Rule));
        Assert.Equal((RuleOrigin.Session, "dotnet ef database update"), (rule.Origin, Outcomes.Present(rule.Target)));
        Assert.Equal([rule], run.Get<IPermissionAudit>().SessionRulesOf(first.Session));
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
    }

    [Fact]
    public async Task AnAutonomousJobRunsACommandUnattendedAndIsDeniedAnEditOutsideItsWorktreeAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "outside-edit", (".avala/permissions.json", Autonomous));

        var decisions = await run.DecisionsAsync(count: 2);

        Assert.Equal(
            [
                (ItemKind.Command, PolicyAnswer.Allow, "autonomous-commands-in-the-worktree"),
                (ItemKind.FileEdit, PolicyAnswer.Deny, "edits-outside-the-workspace-go-to-a-human"),
            ],
            decisions.Select(decision => (decision.Kind, decision.Answer, Outcomes.Present(decision.Rule).Name)));
        Assert.All(decisions, decision => Assert.Equal((DecisionDelivery.Answered, Autonomy.Autonomous), (decision.Delivery, decision.Autonomy)));
        Assert.Contains(await run.TurnAsync(), update => update is ItemCompleted { Item.Value: "note", Outcome: ItemOutcome.Cancelled });
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        Assert.False(File.Exists(Path.Combine(run.Worktree, "..", "avala-outside-note.txt")));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AJobMayAskToBeSupervisedButNeverToBeLooserThanItsRepositoryAsync(bool autonomousRepository, bool refused)
    {
        var requested = refused ? Autonomy.Autonomous : Autonomy.Supervised;
        await using var run = autonomousRepository
            ? await SimulatedRun.StartAsync(plugins, "question", requested, (".avala/permissions.json", Autonomous))
            : await SimulatedRun.StartAsync(plugins, "question", requested);

        var applied = await run.AutonomyAsync();
        var asked = await run.FormDecisionAsync();

        Assert.Equal(
            (run.Job, autonomousRepository ? Autonomy.Autonomous : Autonomy.Supervised, Option<Autonomy>.Some(requested), Autonomy.Supervised, refused),
            (applied.Job, applied.Declared, applied.Requested, applied.Effective, applied.Refused));
        Assert.Equal((DecisionDelivery.LeftToHuman, Autonomy.Supervised), (asked.Delivery, asked.Autonomy));
        Assert.Equal(Option<SessionAutonomy>.Some(applied), run.Get<IPermissionAudit>().AutonomyOf(asked.Session));
    }
}
