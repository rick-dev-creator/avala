using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Contracts;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Permissions.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workbench.Inspector;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Tests.Inspector;

public sealed class InspectorViewModelTests : IDisposable
{
    private readonly Bench bench = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EachSectionShowsItsShareOfTheJobsEvidence()
    {
        var session = SessionId.New();
        var parent = bench.Job("Split the work", JobStatus.Running);
        var job = bench.Job("Fix the failing test", JobStatus.AwaitingReview);
        var child = bench.Job("Write the notes", JobStatus.Approved);
        bench.Catalog.Change(job.Job, history => history with
        {
            Summary = history.Summary with { Parent = parent.Job, Connection = new ConnectionName("claude-work") },
            Sessions = [new SessionRecord(session, [1, 2])],
            Attempts = [new AttemptRecord(1, AttemptOrigin.Initial, AttemptOutcome.Rejected, Option<string>.None, session), new AttemptRecord(2, AttemptOrigin.Retry, AttemptOutcome.Passed, Option<string>.None, session)],
        });
        bench.Catalog.Change(child.Job, history => history with { Summary = history.Summary with { Parent = job.Job } });
        bench.Audit.Reports.AddRange([Report(job.Job, 1, VerificationOutcome.Failed, CheckStatus.Failed, 1), Report(job.Job, 2, VerificationOutcome.Passed, CheckStatus.Passed, 0)]);
        bench.Audit.Decisions.Add(new PolicyDecision(session, TurnId.New(), new ItemId("test"), job.Job, ItemKind.Command, "dotnet test", PolicyAnswer.Allow, new PolicyRule(RuleOrigin.Repository, "tests", ItemKind.Command, "dotnet test*", RuleScope.Anywhere, PolicyAnswer.Allow), DecisionDelivery.Answered, DateTimeOffset.UnixEpoch));
        bench.Audit.Autonomies[session] = new SessionAutonomy(session, job.Job, Autonomy.Autonomous, Autonomy.Supervised, Autonomy.Supervised, false);
        bench.Audit.Budgets[session] = new SessionBudget(session, BudgetFileStatus.Applied, Option<BudgetError>.None, new BudgetCaps([new Cost(1m, "USD")], Option<long>.None, Option<double>.None), Option<FileOrigin>.None);
        bench.Audit.Carve = new BudgetCarve(parent.Job, job.Job, [new Cost(0.5m, "USD")], Option<long>.None, 0.5, DateTimeOffset.UnixEpoch);
        bench.Usage.Jobs[job.Job] = new UsageSummary(new TokenUsage(1200, 300, 0, 0, 0), [new Cost(0.25m, "USD")], 0, default, []);
        var workspace = bench.Workspaces.Known[Outcomes(job)];
        bench.Resources.Leased.Add(new PortLease(workspace.Path, 41000, 41009));
        bench.Resources.Delegations.Add(new DelegationRecord(session, new ItemId("delegate"), "Write the notes", DateTimeOffset.UnixEpoch)
        {
            Parent = job.Job,
            Child = child.Job,
            Report = new ChildReport(child.Job, ChildOutcome.Integrated, JobStatus.Approved, DateTimeOffset.UnixEpoch),
        });
        var inspector = new InspectorViewModel(job.Job, bench.Inspection, bench.Ui);

        await inspector.Request(0)(Cancellation);

        Assert.Equal(
            ("Verified on attempt 2 of 2", "Attempt 1: failed · tests failed (exit 1)|Attempt 2: passed · tests passed (exit 0)"),
            (inspector.Evidence.Summary, string.Join("|", inspector.Evidence.Attempts)));
        Assert.Equal(
            ("1 allowed by rules · 0 answered by you · 0 denied · 0 assumptions", "Allowed Command dotnet test · rule tests"),
            (inspector.Audit.Summary, string.Join("|", inspector.Audit.Decisions)));
        Assert.Equal(
            ("USD 0.25 · 1,500 tokens", "Cost cap USD 1", "Carved 50% of its parent's budget · USD 0.5"),
            (inspector.Usage.Spent, string.Join("|", inspector.Usage.Caps), inspector.Usage.Carve));
        Assert.Equal(
            ("Supervised, tightened from the repository's Autonomous", "claude-work"),
            (inspector.Autonomy.Autonomy, inspector.Autonomy.Connection));
        Assert.Equal(("avala/fix-the-test", "main at ba5eba5", "Ports 41000–41009"), (inspector.Worktree.Branch, inspector.Worktree.Base, inspector.Worktree.Ports));
        Assert.Equal(
            ("Delegated by Split the work", "Write the notes · Approved · Integrated"),
            (inspector.Delegation.Parent, string.Join("|", inspector.Delegation.Children)));
    }

    public void Dispose() => bench.Dispose();

    private WorkspaceId Outcomes(JobSummary job) =>
        bench.Catalog.Summary(job.Job).Workspace.Match(workspace => workspace, () => throw new InvalidOperationException("The job has no worktree."));

    private static VerificationReport Report(JobId job, int attempt, VerificationOutcome outcome, CheckStatus status, int exit) =>
        new(job, attempt, outcome, Option<FileOrigin>.None, [new CheckEvidence("tests", "dotnet test", status, exit, TimeSpan.FromSeconds(1), string.Empty, string.Empty)], GateVerdict.Pass, DateTimeOffset.UnixEpoch);
}
