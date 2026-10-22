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

    private const string Delegates = """{ "delegation": {} }""";

    private const string RetiredEscalation = """{ "delegation": { "escalation": "human", "parentWindowSeconds": 30 } }""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AParentAnswersItsChildsQuestionThroughItsPendingCallAndTheAnswerIsTheParentsAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/checks.json", PassingChecks), (".avala/permissions.json", Autonomous), (".avala/jobs.json", Delegates));
        var forms = run.Watch<FormDecided>();
        var reported = run.Watch<ChildReported>();
        var told = run.Watch<ParentAsked>();
        var activity = run.Watch<AgentActivity>();
        var parent = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-asks-parent] Choose the orders database")));

        var asked = (await forms.UntilAsync(update => update.Decision.Delivery == DecisionDelivery.LeftToParent)).Decision;
        var early = (ToolReturned)(await activity.UntilAsync(update => update.Event is ToolReturned { Item.Value: "delegate-migrate" })).Event;
        var answered = (await forms.UntilAsync(update => update.Decision.Item == asked.Item && update.Decision.Delivery == DecisionDelivery.Answered)).Decision;
        var waited = (ToolCalled)(await activity.UntilAsync(update => update.Event is ToolCalled { Tool: "wait_child" })).Event;
        var report = Outcomes.Present((await reported.UntilAsync(_ => true)).Delegation.Report);
        var delivered = (ToolReturned)(await activity.UntilAsync(update => update.Event is ToolReturned returned && returned.Item == waited.Item)).Event;

        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(parent)));
        Assert.True((await told.UntilAsync(_ => true)).InCall, "The child's question did not reach its parent through the parent's pending delegate call");
        Assert.Contains("\"outcome\":\"asking\"", early.Result.Content, StringComparison.Ordinal);
        Assert.Contains("\"outcome\":\"integrated\"", delivered.Result.Content, StringComparison.Ordinal);
        Assert.Equal((Option<JobId>.Some(parent), Option<JobId>.Some(parent)), (asked.Parent, answered.Parent));
        Assert.Equal(["PostgreSQL"], Assert.Single(Outcomes.Present(answered.Answer).Fields).Chosen);
        Assert.Equal((Outcomes.Present(asked.Job), ChildOutcome.Integrated), (report.Child, report.Outcome));
        Assert.DoesNotContain(run.Get<IPermissionAudit>().FormsOfJob(report.Child), decision => decision.Delivery == DecisionDelivery.LeftToHuman);
    }

    [Fact]
    public async Task AnOlderDelegationSectionThatStillSendsQuestionsToAPersonIsIgnoredAndTheParentIsAskedAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/checks.json", PassingChecks), (".avala/permissions.json", Autonomous), (".avala/jobs.json", RetiredEscalation));
        var forms = run.Watch<FormDecided>();
        var parent = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-asks-parent] Choose the orders database")));

        var asked = (await forms.UntilAsync(update => update.Decision.Job != Option<JobId>.Some(parent))).Decision;
        var delivered = await run.ReportDeliveredAsync();

        Assert.Equal((DecisionDelivery.LeftToParent, Option<JobId>.Some(parent)), (asked.Delivery, asked.Parent));
        Assert.Equal(ChildOutcome.Integrated, Outcomes.Present(delivered.Report).Outcome);
    }

    [Fact]
    public async Task AParentsConversationShowsTheSubAgentWaitingForItsAnswerAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/checks.json", PassingChecks), (".avala/jobs.json", Delegates));
        var told = run.Watch<ParentAsked>();
        var workbench = await run.WorkbenchAsync();
        var parent = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-question] Choose the orders database")));
        _ = await told.UntilAsync(_ => true);

        var conversation = await workbench.SelectAsync(parent);
        await workbench.ShowsAsync(() => OpenWorkbench.Entry(conversation, "ChildAskingViewModel") is not null);

        Assert.Matches("^Sub-agent \".+Choose the.+\" on .+ is waiting for this job$", Assert.Single(await run.Ui.ReadAsync(() => OpenWorkbench.Texts(conversation, "ChildAskingViewModel", "Headline"))));
    }

    [Fact]
    public async Task AParentsConversationNamesTheSubAgentWhosePermissionWaitsForYouAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/checks.json", PassingChecks), (".avala/jobs.json", Delegates));
        var decisions = run.Watch<PermissionDecided>();
        var workbench = await run.WorkbenchAsync();
        var parent = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-waiting] Migrate the orders database")));
        _ = await decisions.UntilAsync(update => update.Decision.Job != Option<JobId>.Some(parent) && update.Decision.Delivery == DecisionDelivery.LeftToHuman);

        var conversation = await workbench.SelectAsync(parent);
        await workbench.ShowsAsync(() => OpenWorkbench.Entry(conversation, "ChildAskingViewModel") is not null);

        Assert.Matches("^Sub-agent \".+Migrate the.+\" on .+ is asking you$", Assert.Single(await run.Ui.ReadAsync(() => OpenWorkbench.Texts(conversation, "ChildAskingViewModel", "Headline"))));
    }

    [Fact]
    public async Task ASubAgentWaitingForItsParentsAnswerSaysSoUnderItsParentInTheSidebarAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/checks.json", PassingChecks), (".avala/jobs.json", Delegates));
        var told = run.Watch<ParentAsked>();
        var workbench = await run.WorkbenchAsync();
        var parent = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-question-blocked] Choose the orders database")));
        _ = await told.UntilAsync(_ => true);

        await workbench.ShowsAsync(() => ChildFact(workbench, parent) == "waiting for its parent");

        Assert.Equal("waiting for its parent", await run.Ui.ReadAsync(() => ChildFact(workbench, parent)));
    }

    [Fact]
    public async Task AParentBlockedOnTwoDelegateCallsHearsEachChildThroughItsCallsAndGetsBothReportsByWaitingAgainAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/checks.json", PassingChecks), (".avala/permissions.json", Autonomous), (".avala/jobs.json", Delegates));
        var delivered = run.Watch<ReportDelivered>();
        var told = run.Watch<ParentAsked>();
        var forms = run.Watch<FormDecided>();
        var parent = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-asks-parent-alongside] Choose the orders and seed databases")));

        var asked = await told.CollectUntilAsync(_ => true);
        asked = [.. asked, .. await told.CollectUntilAsync(_ => true)];
        var reports = await delivered.CollectUntilAsync(_ => true);
        reports = [.. reports, .. await delivered.CollectUntilAsync(_ => true)];
        var answered = 0;
        _ = await forms.CollectUntilAsync(update => update.Decision.Delivery == DecisionDelivery.Answered && update.Decision.Parent == Option<JobId>.Some(parent) && ++answered == 2);

        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(parent)));
        Assert.Equal(2, reports.Select(report => report.Delegation.Child).Distinct().Count());
        Assert.True(asked[0].InCall, "The first question did not reach its parent through its pending delegate calls");
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
                (".avala/jobs.json", """{ "delegation": { "connections": ["sim"] } }"""),
            ]);
        var told = run.Watch<ParentAsked>();
        var forms = run.Watch<FormDecided>();

        var asked = await told.UntilAsync(_ => true);
        var answered = (await forms.UntilAsync(update => update.Decision.Item == asked.Item && update.Decision.Delivery == DecisionDelivery.Answered)).Decision;
        var reported = await run.ReportDeliveredAsync();

        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(run.Job)));
        Assert.True(asked.InCall, "The child's question did not reach Claude Code through its pending delegate call");
        Assert.Equal(Option<JobId>.Some(run.Job), answered.Parent);
        Assert.Equal((ChildOutcome.Integrated, AnswerRoute.ToolResult), (Outcomes.Present(reported.Report).Outcome, Outcomes.Present(reported.Answered).Route));
    }

    [Fact]
    public async Task AChildsPermissionGoesToAPersonWhoAnswersItOnceWhileTheChildKeepsItsWorkAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/checks.json", PassingChecks), (".avala/jobs.json", Delegates));
        var decisions = run.Watch<PermissionDecided>();
        var reported = run.Watch<ChildReported>();
        var parent = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-waiting] Migrate the database")));

        var asked = (await decisions.UntilAsync(update => update.Decision.Job != Option<JobId>.Some(parent) && update.Decision.Delivery == DecisionDelivery.LeftToHuman)).Decision;
        var person = Outcomes.Succeeds(await run.Get<IPermissionAnswers>().AnswerAsync(asked.Session, new PermissionReply(asked.Item, PermissionAnswer.Allow), Cancellation));
        var report = Outcomes.Present((await reported.UntilAsync(_ => true)).Delegation.Report);

        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(parent)));
        Assert.Equal(Option<JobId>.None, asked.Parent);
        Assert.Equal([person], run.Get<IPermissionAudit>().AnswersOfJob(report.Child));
        Assert.Empty(run.Get<IPermissionAudit>().FormsOfJob(report.Child));
        Assert.Equal(ChildOutcome.Integrated, report.Outcome);
    }

    [Fact]
    public async Task AReviewerChildIsDeniedEveryEditByThePolicyAndReportsWithoutChangingItsParentAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/checks.json", PassingChecks), (".avala/permissions.json", Autonomous), (".avala/jobs.json", Delegates));
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
    public async Task AChildsQuestionLeftUnansweredForHoursStillWaitsForItsParentAndNeverGoesToAPersonAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/checks.json", PassingChecks), (".avala/jobs.json", Delegates));
        var told = run.Watch<ParentAsked>();
        var parent = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-question-blocked] Choose the orders database")));

        var waiting = await told.UntilAsync(_ => true);
        run.Clock.Advance(TimeSpan.FromHours(3));
        await run.DeliveredAsync();

        var child = Outcomes.Present(waiting.Delegation.Child);
        var question = Assert.Single(run.Get<IPermissionAudit>().FormsOfJob(child));
        Assert.Equal((DecisionDelivery.LeftToParent, Option<JobId>.Some(parent)), (question.Delivery, question.Parent));
        Assert.True(run.Get<IParentAnswers>().Waits(question.Session, question.Item), "The child's question no longer waits for its parent");
    }

    [Fact]
    public async Task AChildAskingAgainAfterARestartWakesItsDeferredParentInsteadOfAskingAPersonAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/checks.json", PassingChecks), (".avala/jobs.json", Delegates));
        var told = run.Watch<ParentAsked>();
        var parent = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-question-blocked] Choose the orders database")));
        var before = await told.UntilAsync(_ => true);

        await run.RestartAsync(() => told = run.Watch<ParentAsked>());
        var after = await told.UntilAsync(update => update.Woken);

        var child = Outcomes.Present(before.Delegation.Child);
        Assert.Equal((Option<JobId>.Some(parent), Option<JobId>.Some(child)), (after.Delegation.Parent, after.Delegation.Child));
        Assert.NotEqual(before.Session, after.Session);
        Assert.DoesNotContain(run.Get<IPermissionAudit>().FormsOfJob(child), decision => decision.Delivery == DecisionDelivery.LeftToHuman);
        Assert.Single(run.Get<IDelegations>().OfParent(parent));
    }

    private static string ChildFact(OpenWorkbench workbench, JobId parent) =>
        SidebarRows.In(workbench.Sidebar, "Running")
            .Where(row => row["Job"].Value<JobId>() == parent)
            .SelectMany(row => row["Children"].Items)
            .Select(child => child["Fact"].Text)
            .FirstOrDefault() ?? string.Empty;
}
