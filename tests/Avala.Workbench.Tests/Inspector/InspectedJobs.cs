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
using Avala.Sdk.Presentation;
using Avala.Testing;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Tests.Inspector;

internal static class InspectedJobs
{
    public static JobSummary Reviewed(Bench bench)
    {
        var session = SessionId.New();
        var parent = bench.Job("Harden the auth endpoints", JobStatus.Running);
        var job = bench.Job("Rate-limit POST /login", JobStatus.AwaitingReview);
        var child = bench.Job("Write the login tests", JobStatus.Approved);
        bench.Catalog.Change(job.Job, history => history with
        {
            Summary = history.Summary with { Parent = parent.Job, Connection = new ConnectionName("claude-personal") },
            Sessions = [new SessionRecord(session, [1, 2])],
            Attempts = [new AttemptRecord(1, AttemptOrigin.Initial, AttemptOutcome.Rejected, Option<string>.None, session), new AttemptRecord(2, AttemptOrigin.Retry, AttemptOutcome.Passed, Option<string>.None, session)],
        });
        bench.Catalog.Change(child.Job, history => history with { Summary = history.Summary with { Parent = job.Job } });
        bench.Audit.Reports.AddRange([Report(job.Job, 1, VerificationOutcome.Failed, CheckStatus.Failed, 1), Report(job.Job, 2, VerificationOutcome.Passed, CheckStatus.Passed, 0)]);
        bench.Audit.Decisions.Add(new PolicyDecision(session, TurnId.New(), new ItemId("test"), job.Job, ItemKind.Command, "npm test", PolicyAnswer.Allow, new PolicyRule(RuleOrigin.Repository, "tests", ItemKind.Command, "npm test*", RuleScope.Anywhere, PolicyAnswer.Allow), DecisionDelivery.Answered, DateTimeOffset.UnixEpoch));
        bench.Audit.Autonomies[session] = new SessionAutonomy(session, job.Job, Autonomy.Autonomous, Autonomy.Supervised, Autonomy.Supervised, false);
        bench.Audit.Budgets[session] = new SessionBudget(session, BudgetFileStatus.Applied, Option<BudgetError>.None, new BudgetCaps([new Cost(5m, "USD")], Option<long>.None, Option<double>.None), Option<FileOrigin>.None);
        bench.Audit.Carve = new BudgetCarve(parent.Job, job.Job, [new Cost(0.5m, "USD")], Option<long>.None, 0.5, DateTimeOffset.UnixEpoch);
        bench.Usage.Jobs[job.Job] = new UsageSummary(new TokenUsage(1200, 300, 0, 0, 0), [new Cost(0.25m, "USD")], 0, default, []);
        var workspace = bench.Workspaces.Known[bench.Catalog.Summary(job.Job).Workspace.Match(found => found, () => throw new InvalidOperationException("The job has no worktree."))];
        bench.Resources.Leased.Add(new PortLease(workspace.Path, 41000, 41009));
        bench.Resources.Delegations.Add(new DelegationRecord(session, new ItemId("delegate"), "Write the login tests", DateTimeOffset.UnixEpoch)
        {
            Parent = job.Job,
            Child = child.Job,
            Report = new ChildReport(child.Job, ChildOutcome.Integrated, JobStatus.Approved, DateTimeOffset.UnixEpoch),
        });

        return bench.Catalog.Summary(job.Job);
    }

    public static Task<Presented> FocusAsync<TSection>(this TSection section, Option<JobId> job, Bench bench)
        where TSection : IPresentation, Sdk.Regions.IRegionAware<JobId>, IActivatable =>
        section.PresentsAfterAsync(
            () => bench.Post(() =>
            {
                section.Activate();
                section.OnRegionContextChanged(job);
            }),
            () => $"revision {section.Revision}",
            TestContext.Current.CancellationToken);

    private static VerificationReport Report(JobId job, int attempt, VerificationOutcome outcome, CheckStatus status, int exit) =>
        new(job, attempt, outcome, Option<FileOrigin>.None, [new CheckEvidence("tests", "npm test", status, exit, TimeSpan.FromSeconds(1), string.Empty, string.Empty)], GateVerdict.Pass, DateTimeOffset.UnixEpoch);
}
