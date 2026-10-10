using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class WorkbenchReviewTests(PublishedPlugins plugins)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AVerifiedJobIsReviewedAndApprovedWithKeepFromTheReviewSheetAsync()
    {
        await using var run = await VerifiedAsync();
        var (workbench, review) = await ReviewAsync(run);

        var (verdict, failed, file) = await run.Ui.ReadAsync(() => (
            review["Verdict"].Text,
            review["Exceptions"].Items.Select(exception => (exception["Title"].Text, exception["Fact"].Text)).FirstOrDefault(),
            review["Files"].Items.Select(changed => (changed["Path"].Text, changed["Counts"].Text)).Single()));
        Assert.Equal("Verified on attempt 2 of 2", verdict);
        Assert.Equal("Attempt 1 failed", failed.Item1);
        Assert.StartsWith("calculator", failed.Item2, StringComparison.Ordinal);
        Assert.Equal(("calculator.txt", "+1"), file);

        await run.Ui.RunAsync(() => review["Files"].Items[0].ExecuteAsync("ShowHunksCommand"));
        Assert.Contains("+add(2, 2) = 4", await run.Ui.ReadAsync(() => Assert.Single(review["Files"].Items[0]["Hunks"].Items)["Lines"].Items.Select(line => line["Text"].Text).ToList()));

        await run.Ui.RunAsync(() => review.ExecuteAsync("ApproveCommand"));
        await workbench.ShowsAsync(() => review["Status"].Value<JobStatus>() == JobStatus.Approved);
        Assert.StartsWith("Approved: the branch avala/", await run.Ui.ReadAsync(() => review["Outcome"].Text), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendingAJobBackFromTheReviewSheetStartsANewRoundWithTheFeedbackAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "reply");
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var (workbench, review) = await ReviewAsync(run);

        await run.Ui.RunAsync(() =>
        {
            review.Set("Feedback", "Please greet the new hires too.");

            return review.ExecuteAsync("SendBackCommand");
        });

        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var conversation = await run.Ui.ReadAsync(() => workbench.Page["Conversation"]);
        await workbench.ShowsAsync(() => OpenWorkbench.Texts(conversation, "PromptViewModel", "Origin").Count == 2);
        Assert.Equal(["Instruction", "Sent back"], await run.Ui.ReadAsync(() => OpenWorkbench.Texts(conversation, "PromptViewModel", "Origin")));
        Assert.Equal("Sent back with your feedback", await run.Ui.ReadAsync(() => review["Outcome"].Text));
    }

    [Fact]
    public async Task AnApprovalRefusedForAConflictShowsTheConflictingFilesOnTheSheetAsync()
    {
        await using var run = await VerifiedAsync((".avala/jobs.json", ReviewTests.MergeFile));
        await run.Repository.CommitAsync("calculator.txt", "add(2, 2) = 22\n", Cancellation);
        var (_, review) = await ReviewAsync(run);

        await run.Ui.RunAsync(() => review.ExecuteAsync("ApproveCommand"));

        Assert.Equal(
            ("The work conflicts with the base branch in calculator.txt.", "calculator.txt", JobStatus.AwaitingReview),
            await run.Ui.ReadAsync(() => (review["Refusal"].Text, string.Join(",", review["Conflicts"].Value<IReadOnlyList<string>>()), review["Status"].Value<JobStatus>())));
    }

    [Fact]
    public async Task TheDecisionsPopoverAnswersAPermissionAndAFormOfTwoJobsAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "permission");
        var question = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("question"))));
        Assert.Equal(DecisionDelivery.LeftToHuman, (await run.DecisionAsync()).Delivery);
        Assert.Equal(DecisionDelivery.LeftToHuman, (await run.FormDecisionAsync()).Delivery);
        var workbench = await run.WorkbenchAsync();
        var decisions = await run.Ui.ReadAsync(() => workbench.Toolbar["Decisions"]);
        await workbench.ShowsAsync(() => decisions["Items"].Items.Count == 2);
        var kinds = await run.Ui.ReadAsync(() => decisions["Items"].Items.Select(item => item["Card"].Kind).Order(StringComparer.Ordinal).ToList());

        await run.Ui.RunAsync(() => decisions.ExecuteAsync("AnswerCommand"));
        await workbench.ShowsAsync(() => decisions["Items"].Items.Count == 1);
        await run.Ui.RunAsync(() => decisions.ExecuteAsync("AnswerCommand"));

        Assert.Equal(["FormCardViewModel", "PermissionCardViewModel"], kinds);
        Assert.Equal([JobStatus.AwaitingReview, JobStatus.AwaitingReview], await run.SettledAsync(run.Job, question));
        await workbench.ShowsAsync(() => decisions["IsEmpty"].Value<bool>());
    }

    [Fact]
    public async Task AMultiLineCommandIsAskedAndItsDenialReviewedUnderTheSameHonestTitleAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "script-permission");
        Assert.Equal(DecisionDelivery.LeftToHuman, (await run.DecisionAsync()).Delivery);
        var workbench = await run.WorkbenchAsync();
        var decisions = await run.Ui.ReadAsync(() => workbench.Toolbar["Decisions"]);
        await workbench.ShowsAsync(() => decisions["Items"].Items.Count == 1);
        var (title, writes, target) = await run.Ui.ReadAsync(() =>
        {
            var item = decisions["Items"].Items[0];

            return (item["Title"].Text, item["Writes"].Text, item["Target"].Text);
        });

        await run.Ui.RunAsync(() => decisions.ExecuteAsync("DenyCommand"));

        Assert.Equal(("Run 4 commands: node, cat > /tmp/avala-smoke.js, rm", "Writes to /tmp/avala-smoke.js"), (title, writes));
        Assert.Contains("console.log(page.start());", target, StringComparison.Ordinal);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var (_, review) = await ReviewAsync(run);
        var denial = await run.Ui.ReadAsync(() => review["Exceptions"].Items.Select(exception => (exception["Title"].Text, exception["Output"].Text)).Single(exception => exception.Item1.StartsWith("You denied", StringComparison.Ordinal)));
        Assert.Equal(("You denied: run 4 commands: node, cat > /tmp/avala-smoke.js, rm", target), denial);
    }

    [Fact]
    public async Task TheInspectorShowsTheEvidenceDecisionsAndUsageOfAFinishedJobAsync()
    {
        await using var run = await VerifiedAsync();
        await run.UsageRecordedAsync(reports: 1);
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(run.Job);

        var (usage, evidence, audit) = (workbench.Section("UsageSectionViewModel"), workbench.Section("EvidenceSectionViewModel"), workbench.Section("AuditSectionViewModel"));
        await run.Ui.InvokeAsync(() => workbench.Page.Execute("ToggleInspectorCommand"), Cancellation);

        foreach (var section in new[] { usage, evidence, audit })
        {
            await run.Ui.PresentedAsync(section.Presentation, () => section["IsLoaded"].Value<bool>(), () => $"{section.Kind} did not load");
        }

        var (spent, summary, attempts, decisions) = await run.Ui.ReadAsync(() => (
            usage["Spent"].Text,
            evidence["Summary"].Text,
            evidence["Attempts"].Value<IReadOnlyList<string>>(),
            audit["Decisions"].Value<IReadOnlyList<string>>()));
        Assert.EndsWith("tokens", spent, StringComparison.Ordinal);
        Assert.Equal("Verified on attempt 2 of 2", summary);
        Assert.Equal(2, attempts.Count);
        Assert.StartsWith("Attempt 1: failed", attempts[0], StringComparison.Ordinal);
        Assert.Contains(decisions, decision => decision.EndsWith("· rule tests", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StoppingAJobKeepsItsWorktreeAndTheJobCanBeContinuedAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "hang");
        var workbench = await run.WorkbenchAsync();
        var composer = (await workbench.SelectAsync(run.Job))["Composer"];
        await run.ResumableAsync();
        await workbench.ShowsInGroupAsync(run.Job, "Running", "working");

        await run.Ui.RunAsync(() => composer.ExecuteAsync("StopCommand"));

        var hold = await run.HoldAsync();
        Assert.Equal(JobStatus.NeedsHelp, await run.SettledAsync());
        await workbench.ShowsInGroupAsync(run.Job, "NeedsYou", "stopped");
        Assert.Equal((HoldReason.Stopped, SessionHalt.Stopped), (hold.Reason, hold.Halt));
        Assert.True(Directory.Exists(run.Worktree));
        await run.Ui.RunAsync(() =>
        {
            composer.Set("Draft", "Carry on where you stopped.");

            return composer.ExecuteAsync("SendCommand");
        });
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await workbench.ShowsInGroupAsync(run.Job, "ReadyForReview");
    }

    private Task<SimulatedRun> VerifiedAsync(params (string Path, string Content)[] committed) =>
        SettledAsync(SimulatedRun.StartAsync(
            plugins,
            "fix-after-feedback",
            [(".avala/checks.json", ReviewTests.CalculatorChecks), (".avala/permissions.json", ReviewTests.TestsPolicy), .. committed]));

    private static async Task<SimulatedRun> SettledAsync(Task<SimulatedRun> starting)
    {
        var run = await starting;
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());

        return run;
    }

    private static async Task<(OpenWorkbench Workbench, Bound Review)> ReviewAsync(SimulatedRun run)
    {
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() => ((System.Windows.Input.ICommand)workbench.Page["OpenReviewCommand"].Target).CanExecute(null));

        await run.Ui.RunAsync(() => workbench.Page.ExecuteAsync("OpenReviewCommand"));

        var review = await run.Ui.ReadAsync(() => workbench.Page["Review"]);
        Assert.True(await run.Ui.ReadAsync(() => review["IsLoaded"].Value<bool>()));

        return (workbench, review);
    }
}
