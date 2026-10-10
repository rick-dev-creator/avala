using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Host.Tests;

public sealed class ChildEscalationTests(PublishedPlugins plugins)
{
    private const string PassingChecks = """{ "checks": [ { "name": "git", "command": "git", "arguments": ["--version"], "timeoutSeconds": 60 } ] }""";

    private const string Autonomous = """{ "autonomy": "autonomous" }""";

    private const string AsksParent = """{ "delegation": { "escalation": "parent", "parentWindowSeconds": 600 } }""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AParentAllowsWhatItsOwnPolicyAllowsForItsSupervisedChildAndTheAnswerIsAuditedInBothAuditsAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/checks.json", PassingChecks), (".avala/permissions.json", Autonomous), (".avala/jobs.json", AsksParent));
        var decisions = run.Watch<PermissionDecided>();
        var reported = run.Watch<ChildReported>();
        var told = run.Watch<ParentAsked>();
        var activity = run.Watch<AgentActivity>();
        var parent = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-asks-parent] Migrate the database")));

        var asked = (await decisions.UntilAsync(update => update.Decision.Delivery == DecisionDelivery.LeftToParent)).Decision;
        var early = (ToolReturned)(await activity.UntilAsync(update => update.Event is ToolReturned { Item.Value: "delegate-migrate" })).Event;
        var answered = (await decisions.UntilAsync(update => update.Decision.Item == asked.Item && update.Decision.Delivery == DecisionDelivery.Answered)).Decision;
        var waited = (ToolCalled)(await activity.UntilAsync(update => update.Event is ToolCalled { Tool: "wait_child" })).Event;
        var report = Outcomes.Present((await reported.UntilAsync(_ => true)).Delegation.Report);
        var delivered = (ToolReturned)(await activity.UntilAsync(update => update.Event is ToolReturned returned && returned.Item == waited.Item)).Event;

        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(parent)));
        Assert.True((await told.UntilAsync(_ => true)).InCall, "The child's request did not reach its parent through the parent's pending delegate call");
        Assert.Contains("\"outcome\":\"asking\"", early.Result.Content, StringComparison.Ordinal);
        Assert.Contains("\"outcome\":\"integrated\"", delivered.Result.Content, StringComparison.Ordinal);
        Assert.Equal((Option<JobId>.Some(parent), PolicyAnswer.Ask), (asked.Parent, asked.Answer));
        Assert.Equal(Option<JobId>.Some(parent), answered.Parent);
        var child = Outcomes.Present(asked.Job);
        var answer = Assert.Single(run.Get<IPermissionAudit>().AnswersOfJob(child));
        Assert.Equal((PermissionAnswer.Allow, Option<JobId>.Some(parent), "dotnet ef database update"), (answer.Answer, answer.Parent, answer.Target));
        Assert.Equal(answer, Assert.Single(run.Get<IPermissionAudit>().AnswersGivenBy(parent)));
        Assert.Equal((child, ChildOutcome.Integrated), (report.Child, report.Outcome));
    }

    [Fact]
    public async Task AParentsConversationShowsTheSubAgentWaitingForItsAnswerAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/checks.json", PassingChecks), (".avala/jobs.json", AsksParent));
        var told = run.Watch<ParentAsked>();
        var workbench = await run.WorkbenchAsync();
        var parent = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-waiting] Migrate the orders database")));
        _ = await told.UntilAsync(_ => true);

        var conversation = await workbench.SelectAsync(parent);
        await workbench.ShowsAsync(() => OpenWorkbench.Entry(conversation, "ChildAskingViewModel") is not null);

        Assert.Matches("^Sub-agent \".+Migrate the.+\" is waiting for this job$", Assert.Single(await run.Ui.ReadAsync(() => OpenWorkbench.Texts(conversation, "ChildAskingViewModel", "Headline"))));
    }

    [Fact]
    public async Task AParentBlockedOnTwoDelegateCallsHearsEachChildThroughItsCallsAndGetsBothReportsByWaitingAgainAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/checks.json", PassingChecks), (".avala/permissions.json", Autonomous), (".avala/jobs.json", AsksParent));
        var delivered = run.Watch<ReportDelivered>();
        var told = run.Watch<ParentAsked>();
        var parent = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-asks-parent-alongside] Migrate and seed the database")));

        var asked = await told.CollectUntilAsync(_ => true);
        asked = [.. asked, .. await told.CollectUntilAsync(_ => true)];
        var reports = await delivered.CollectUntilAsync(_ => true);
        reports = [.. reports, .. await delivered.CollectUntilAsync(_ => true)];

        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(parent)));
        Assert.Equal(2, reports.Select(report => report.Delegation.Child).Distinct().Count());
        Assert.True(asked[0].InCall, "The first request did not reach its parent through its pending delegate calls");
        Assert.Equal(2, run.Get<IPermissionAudit>().AnswersGivenBy(parent).Count);
        Assert.All(reports, report =>
        {
            Assert.Equal(ChildOutcome.Integrated, Outcomes.Present(report.Delegation.Report).Outcome);
            Assert.Equal(AnswerRoute.ToolResult, Outcomes.Present(report.Delegation.Answered).Route);
        });
    }

    [Fact]
    public async Task AClaudeCodeParentBlockedInItsDelegateCallIsAskedThroughTheCallAnswersAndWaitsForTheReportAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("delegate-asks-parent");
        await using var run = await SimulatedRun.TranscribedAsync(
            plugins,
            claude.Plugin,
            new JobRequest(string.Empty, "Have a supervised sub-agent migrate the database and answer what it asks you."),
            claude.Data,
            [
                (".avala/checks.json", PassingChecks),
                (".avala/permissions.json", Autonomous),
                (".avala/jobs.json", """{ "delegation": { "connections": ["sim"], "escalation": "parent", "parentWindowSeconds": 600 } }"""),
            ]);
        var told = run.Watch<ParentAsked>();
        var decisions = run.Watch<PermissionDecided>();

        var asked = await told.UntilAsync(_ => true);
        var answered = (await decisions.UntilAsync(update => update.Decision.Item == asked.Item && update.Decision.Delivery == DecisionDelivery.Answered)).Decision;
        var reported = await run.ReportDeliveredAsync();

        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(run.Job)));
        Assert.True(asked.InCall, "The child's request did not reach Claude Code through its pending delegate call");
        Assert.Equal(Option<JobId>.Some(run.Job), answered.Parent);
        Assert.Equal((ChildOutcome.Integrated, AnswerRoute.ToolResult), (Outcomes.Present(reported.Report).Outcome, Outcomes.Present(reported.Answered).Route));
        Assert.Single(run.Get<IPermissionAudit>().AnswersGivenBy(run.Job));
    }

    [Fact]
    public async Task AChildRequestBeyondItsParentsOwnRulesGoesToAPersonWhoAnswersItOnceAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/checks.json", PassingChecks), (".avala/jobs.json", AsksParent));
        var decisions = run.Watch<PermissionDecided>();
        var activity = run.Watch<AgentActivity>();
        var reported = run.Watch<ChildReported>();
        var parent = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-asks-parent] Migrate the database")));

        var passed = (await decisions.UntilAsync(update => update.Decision.Passed.IsSome)).Decision;
        var refused = (ToolReturned)(await activity.UntilAsync(update => update.Event is ToolReturned { Result.IsError: true })).Event;
        var person = Outcomes.Succeeds(await run.Get<IPermissionAnswers>().AnswerAsync(passed.Session, new PermissionReply(passed.Item, PermissionAnswer.Allow), Cancellation));
        var report = Outcomes.Present((await reported.UntilAsync(_ => true)).Delegation.Report);

        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(parent)));
        Assert.Equal((DecisionDelivery.LeftToHuman, Option<PassReason>.Some(PassReason.BeyondParent)), (passed.Delivery, passed.Passed));
        Assert.Contains("\"refused\":\"beyondYourRules\"", refused.Result.Content, StringComparison.Ordinal);
        Assert.Equal(Option<JobId>.None, person.Parent);
        Assert.Equal([person], run.Get<IPermissionAudit>().AnswersOfJob(report.Child));
        Assert.Empty(run.Get<IPermissionAudit>().AnswersGivenBy(parent));
        Assert.Equal(ChildOutcome.Integrated, report.Outcome);
    }

    [Fact]
    public async Task AReviewerChildIsDeniedEveryEditByThePolicyAndReportsWithoutChangingItsParentAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/checks.json", PassingChecks), (".avala/permissions.json", Autonomous), (".avala/jobs.json", """{ "delegation": {} }"""));
        var decisions = run.Watch<PermissionDecided>();
        var autonomies = run.Watch<AutonomyApplied>();
        var reported = run.Watch<ChildReported>();
        var parent = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-review] Review the notes")));

        var denied = (await decisions.UntilAsync(update => update.Decision.Kind == ItemKind.FileEdit)).Decision;
        var record = (await reported.UntilAsync(_ => true)).Delegation;
        var report = Outcomes.Present(record.Report);
        var applied = (await autonomies.UntilAsync(update => update.Autonomy.Job == report.Child)).Autonomy;

        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(parent)));
        Assert.Equal((PolicyAnswer.Deny, "read-only-denies-edits"), (denied.Answer, Outcomes.Present(denied.Rule).Name));
        Assert.Equal((ChildRole.Reviewer, ChildOutcome.Reported, JobStatus.Discarded), (record.Role, report.Outcome, report.Status));
        Assert.True(applied.ReadOnly, "The reviewer child's autonomy does not say it is read-only");
        var worktree = Outcomes.Present(Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(parent, Cancellation)).Summary.Workspace);
        var changes = Outcomes.Succeeds(await run.Get<IWorkspaceChanges>().DiffAsync(worktree, Cancellation));
        Assert.Empty(changes.Files);
    }

    [Fact]
    public async Task AParentThatDoesNotAnswerWithinItsWindowLeavesTheRequestToAPersonAndNothingIsAllowedAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/checks.json", PassingChecks), (".avala/jobs.json", AsksParent));
        var asked = run.Watch<ParentAsked>();
        var decisions = run.Watch<PermissionDecided>();
        var parent = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-waiting] Migrate the database")));

        var waiting = await asked.UntilAsync(_ => true);
        run.AdvanceTo(waiting.Until);
        var passed = (await decisions.UntilAsync(update => update.Decision.Passed.IsSome)).Decision;

        Assert.Equal((DecisionDelivery.LeftToHuman, Option<PassReason>.Some(PassReason.ParentTimedOut)), (passed.Delivery, passed.Passed));
        Assert.Equal((Option<JobId>.Some(parent), waiting.Item), (waiting.Delegation.Parent, passed.Item));
        Assert.StartsWith("wants to run a command: dotnet ef database update", waiting.Asking, StringComparison.Ordinal);
        Assert.Empty(run.Get<IPermissionAudit>().AnswersOfJob(Outcomes.Present(passed.Job)));
        Assert.DoesNotContain(run.Get<IPermissionAudit>().OfJob(Outcomes.Present(passed.Job)), decision => decision.Answer == PolicyAnswer.Allow);
    }

    [Fact]
    public async Task AChildWaitingOnItsParentAtARestartAsksAgainAndAPersonAnswersItOnceAsItsDeferredParentCannotAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/checks.json", PassingChecks), (".avala/jobs.json", AsksParent));
        var parent = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-waiting] Migrate the database")));
        var before = await run.DecisionAsync();
        Assert.Equal(DecisionDelivery.LeftToParent, before.Delivery);

        await run.RestartAsync();
        var decisions = run.Watch<PermissionDecided>();
        var passed = (await decisions.UntilAsync(update => update.Decision.Passed.IsSome)).Decision;
        Outcomes.Succeeds(await run.Get<IPermissionAnswers>().AnswerAsync(passed.Session, new PermissionReply(passed.Item, PermissionAnswer.Allow), Cancellation));
        var delivered = await run.ReportDeliveredAsync();

        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(parent));
        Assert.Equal(Option<PassReason>.Some(PassReason.ParentUnreachable), passed.Passed);
        Assert.NotEqual(before.Session, passed.Session);
        var child = Outcomes.Present(passed.Job);
        Assert.Single(run.Get<IPermissionAudit>().AnswersOfJob(child));
        Assert.Equal((Option<JobId>.Some(child), AnswerRoute.Message), (delivered.Child, Outcomes.Present(delivered.Answered).Route));
        Assert.Single(run.Get<IDelegations>().OfParent(parent));
    }
}
