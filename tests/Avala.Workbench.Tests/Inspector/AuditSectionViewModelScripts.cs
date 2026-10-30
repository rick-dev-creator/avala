using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Workbench.Inspector;

namespace Avala.Workbench.Tests.Inspector;

public sealed class AuditSectionViewModelScripts : IDisposable
{
    private readonly Bench bench = new();

    [Fact]
    public async Task AJobInFocusCountsOnlyWhatWasDecidedAndListsEachDecision()
    {
        using var section = new AuditSectionViewModel(bench.Inspected());

        await section.FocusAsync(InspectedJobs.Reviewed(bench).Job, bench);

        Assert.Equal(("1 allowed by rules", string.Empty), (section.Fact, section.Summary));
        Assert.Equal([new DecisionRow(DecisionVerdict.Allowed, "Run", "npm test", "allowed by rule tests")], section.Decisions);
        Assert.Empty(section.Assumptions);
    }

    [Fact]
    public async Task APersonsDenialCountsAsDeniedAndSaysWhoAnswered()
    {
        var job = bench.Job("Fix flaky CheckoutForm test", JobStatus.Running);
        bench.Audit.Answers.Add(new HumanAnswer(SessionId.New(), job.Job, new ItemId("run"), ItemKind.Command, "rm -rf node_modules", PermissionAnswer.Deny, Option<string>.None, Option<PolicyRule>.None, DateTimeOffset.UnixEpoch));
        using var section = new AuditSectionViewModel(bench.Inspected());

        await section.FocusAsync(job.Job, bench);

        Assert.Equal("1 answered by you · 1 denied", section.Summary);
        Assert.Equal([new DecisionRow(DecisionVerdict.Denied, "Run", "rm -rf node_modules", "denied by you")], section.Decisions);
    }

    [Fact]
    public async Task RepeatedDecisionsShowOnceWithHowManyTimesAndDenialsComeFirst()
    {
        var session = SessionId.New();
        var job = Sessioned(session);
        foreach (var item in new[] { "a", "b", "c" })
        {
            bench.Audit.Decisions.Add(Decided(session, job.Job, item, "npm test", PolicyAnswer.Allow));
        }

        bench.Audit.Decisions.Add(Decided(session, job.Job, "d", "curl https://example.com", PolicyAnswer.Deny));
        using var section = new AuditSectionViewModel(bench.Inspected());

        await section.FocusAsync(job.Job, bench);

        Assert.Equal(
            [("curl https://example.com", "Run · denied by default", string.Empty), ("npm test", "Run · allowed by default", "×3")],
            section.Decisions.Select(row => (row.Target, row.Caption, row.Count)));
    }

    [Fact]
    public async Task AQuestionYouAnsweredShowsOnlyYourAnswer()
    {
        var session = SessionId.New();
        var job = Sessioned(session);
        bench.Audit.Decisions.Add(Decided(session, job.Job, "migrate", "dotnet ef database update", PolicyAnswer.Ask) with { Delivery = DecisionDelivery.LeftToHuman });
        bench.Audit.Answers.Add(new HumanAnswer(session, job.Job, new ItemId("migrate"), ItemKind.Command, "dotnet ef database update", PermissionAnswer.Allow, Option<string>.None, Option<PolicyRule>.None, DateTimeOffset.UnixEpoch));
        using var section = new AuditSectionViewModel(bench.Inspected());

        await section.FocusAsync(job.Job, bench);

        Assert.Equal([new DecisionRow(DecisionVerdict.Allowed, "Run", "dotnet ef database update", "allowed by you")], section.Decisions);
    }

    [Fact]
    public async Task ALongListShowsTheFirstFewUntilYouAskForTheRest()
    {
        var session = SessionId.New();
        var job = Sessioned(session);
        foreach (var index in Enumerable.Range(0, 8))
        {
            bench.Audit.Decisions.Add(Decided(session, job.Job, $"item-{index}", $"npm run step-{index}", PolicyAnswer.Allow));
        }

        using var section = new AuditSectionViewModel(bench.Inspected());
        await section.FocusAsync(job.Job, bench);
        var first = (section.Decisions.Count, section.More);

        section.ShowAllCommand.Execute(null);

        Assert.Equal((5, "Show 3 more"), first);
        Assert.Equal((8, string.Empty, false), (section.Decisions.Count, section.More, section.ShowAllCommand.CanExecute(null)));
    }

    private JobSummary Sessioned(SessionId session)
    {
        var job = bench.Job("Fix flaky CheckoutForm test", JobStatus.Running);
        bench.Catalog.Change(job.Job, history => history with { Sessions = [new SessionRecord(session, [1])] });

        return job;
    }

    private static PolicyDecision Decided(SessionId session, JobId job, string item, string target, PolicyAnswer answer) =>
        new(session, TurnId.New(), new ItemId(item), job, ItemKind.Command, target, answer, Option<PolicyRule>.None, DecisionDelivery.Answered, DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task WithNothingInFocusTheSectionIsEmpty()
    {
        using var section = new AuditSectionViewModel(bench.Inspected());

        await section.FocusAsync(Option<JobId>.None, bench);

        Assert.Equal((false, string.Empty, 0), (section.IsLoaded, section.Summary, section.Decisions.Count));
    }

    [Fact]
    public async Task TheHeaderFactLeadsWithDenialsInAmberOtherwiseWhatTheRulesAllowed()
    {
        using var section = new AuditSectionViewModel(bench.Inspected());
        await section.FocusAsync(InspectedJobs.Reviewed(bench).Job, bench);
        var reviewed = (section.Fact, section.IsAttention);
        var denied = bench.Job("Fix flaky CheckoutForm test", JobStatus.Running);
        bench.Audit.Answers.Add(new HumanAnswer(SessionId.New(), denied.Job, new ItemId("run"), ItemKind.Command, "rm -rf node_modules", PermissionAnswer.Deny, Option<string>.None, Option<PolicyRule>.None, DateTimeOffset.UnixEpoch));

        await section.FocusAsync(denied.Job, bench);

        Assert.Equal(("1 allowed by rules", false), reviewed);
        Assert.Equal(("1 denied", true), (section.Fact, section.IsAttention));
    }

    public void Dispose() => bench.Dispose();
}
