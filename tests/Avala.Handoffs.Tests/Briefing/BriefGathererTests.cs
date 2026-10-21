using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Handoffs.Briefing;
using Avala.Handoffs.Tests.Watching;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Handoffs.Tests.Briefing;

public sealed class BriefGathererTests
{
    private static readonly SessionId Session = SessionId.New();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(TurnOutcome.LimitReached, true)]
    [InlineData(TurnOutcome.Interrupted, true)]
    [InlineData(TurnOutcome.Finished, false)]
    public async Task WhatThePersonSentDuringATurnThatDidNotFinishIsInTheBriefOfTheSessionThatContinuesTheJobAsync(TurnOutcome ended, bool listed)
    {
        const string Sent = "Also keep the old rounding for negative amounts.";
        await using var watched = new Watched();
        var notes = new JobNotes(watched.Audit);
        var turn = TurnId.New();
        await notes.HandleAsync(new JobSessionStarted(watched.Job, Session), Cancellation);
        foreach (var activity in new IAgentEvent[] { new TurnStarted(Session, turn), new MessageQueued(Session, turn, Sent), new TurnCompleted(Session, turn, ended) })
        {
            await notes.HandleAsync(new Avala.Agents.Contracts.AgentActivity(activity), Cancellation);
        }

        var history = Outcomes.Present(await watched.Catalog.HistoryAsync(watched.Job, Cancellation));
        var brief = await new BriefGatherer(watched.Worktrees, watched.Audit, watched.Audit, notes)
            .WriteAsync(new BriefRequest(history, Watched.Work, Watched.Personal, Option<Avala.Handoffs.Contracts.LimitReason>.None, Option<string>.None), Cancellation);

        Assert.Equal(listed, brief.Contains($"- {Sent}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheBriefListsTheDecisionsTakenOnTheJobAndTheQuestionsStillOpenAsync()
    {
        await using var watched = new Watched();
        var job = watched.Job;
        var tests = new PolicyRule(RuleOrigin.Repository, "tests", ItemKind.Command, Option<string>.None, RuleScope.Workspace, PolicyAnswer.Allow);
        watched.Audit.Decisions.AddRange(
        [
            Decision(job, "allowed", ItemKind.Command, "dotnet test", PolicyAnswer.Allow, DecisionDelivery.Answered) with { Rule = tests },
            Decision(job, "denied", ItemKind.FileEdit, "secrets.txt", PolicyAnswer.Deny, DecisionDelivery.Answered),
            Decision(job, "asked", ItemKind.Command, "rm -rf build", PolicyAnswer.Ask, DecisionDelivery.LeftToHuman),
            Decision(job, "open", ItemKind.Web, "https://example.com", PolicyAnswer.Ask, DecisionDelivery.LeftToHuman),
            Decision(job, "lost", ItemKind.Web, "https://example.com/lost", PolicyAnswer.Ask, DecisionDelivery.Undelivered),
            Decision(JobId.New(), "other", ItemKind.Command, "make", PolicyAnswer.Allow, DecisionDelivery.Answered),
        ]);
        watched.Audit.Answers.AddRange(
        [
            Answer(job, "asked", ItemKind.Command, "rm -rf build", PermissionAnswer.Allow),
            Answer(job, "blocked", ItemKind.Web, "https://example.com/blocked", PermissionAnswer.Deny),
        ]);
        watched.Audit.Forms.AddRange(
        [
            Form(job, "Choose a database", "The service stores orders.", DecisionDelivery.Answered) with { Answer = new FormAnswer(new ItemId("database"), []) },
            Form(job, "Choose a cache", "The service needs a cache.", DecisionDelivery.LeftToHuman),
            Form(job, "Name the branch", "Any name will do.", DecisionDelivery.Answered),
        ]);
        var history = Outcomes.Present(await watched.Catalog.HistoryAsync(job, Cancellation));

        var brief = await watched.Briefs.WriteAsync(new BriefRequest(history, Watched.Work, Watched.Personal, Option<Avala.Handoffs.Contracts.LimitReason>.None, Option<string>.None), Cancellation);

        Assert.Contains(
            string.Join(
                Environment.NewLine,
                "## Decisions taken",
                "- Allowed by the rule tests: run dotnet test",
                "- Denied: edit secrets.txt",
                "- A person allowed: run rm -rf build",
                "- A person denied: reach https://example.com/blocked",
                "- Answered for the agent: Choose a database",
                string.Empty,
                "## Open questions",
                "- Unanswered permission: reach https://example.com",
                "- Choose a cache: The service needs a cache.",
                string.Empty),
            brief,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryKindOfRequestTheBriefNamesIsWordedNeverItsCodeAsync()
    {
        await using var watched = new Watched();
        var job = watched.Job;
        var kinds = Enum.GetValues<ItemKind>();
        watched.Audit.Decisions.AddRange(kinds.SelectMany(kind => new[]
        {
            Decision(job, $"answered-{kind}", kind, "target", PolicyAnswer.Deny, DecisionDelivery.Answered),
            Decision(job, $"open-{kind}", kind, "target", PolicyAnswer.Ask, DecisionDelivery.LeftToHuman),
        }));
        watched.Audit.Answers.AddRange(kinds.Select(kind => Answer(job, $"asked-{kind}", kind, "target", PermissionAnswer.Allow)));
        var history = Outcomes.Present(await watched.Catalog.HistoryAsync(job, Cancellation));

        var brief = await watched.Briefs.WriteAsync(new BriefRequest(history, Watched.Work, Watched.Personal, Option<Avala.Handoffs.Contracts.LimitReason>.None, Option<string>.None), Cancellation);

        Assert.DoesNotContain(kinds, kind => brief.Contains(kind.ToString(), StringComparison.Ordinal) || brief.Contains(kind.ToString().ToLowerInvariant(), StringComparison.Ordinal) && kind.ToString().Skip(1).Any(char.IsUpper));
    }

    private static PolicyDecision Decision(JobId job, string item, ItemKind kind, string target, PolicyAnswer answer, DecisionDelivery delivery) =>
        new(Session, TurnId.New(), new ItemId(item), job, kind, target, answer, Option<PolicyRule>.None, delivery, Watched.Start);

    private static HumanAnswer Answer(JobId job, string item, ItemKind kind, string target, PermissionAnswer answer) =>
        new(Session, job, new ItemId(item), kind, target, answer, Option<string>.None, Option<PolicyRule>.None, Watched.Start);

    private static FormDecision Form(JobId job, string title, string context, DecisionDelivery delivery) =>
        new(Session, TurnId.New(), new ItemId(title), job, new AgentForm(FormPurpose.Question, title, context, []), Autonomy.Supervised, Option<FormAnswer>.None, [], delivery, Watched.Start);
}
