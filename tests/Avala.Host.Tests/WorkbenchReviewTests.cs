using Avala.Jobs.Contracts;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class WorkbenchReviewTests(PublishedPlugins plugins)
{
    private static readonly string[] Groups = ["NeedsYou", "Running", "ReadyForReview", "Done"];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AVerifiedJobIsReviewedAndApprovedWithKeepFromTheReviewSheetAsync()
    {
        await using var run = await VerifiedAsync();
        var review = await ReviewAsync(run);

        var (verdict, failed, file) = await run.Ui.ReadAsync(() => (
            review["Verdict"].Text,
            review["Exceptions"].Items.Select(exception => exception["Title"].Text).FirstOrDefault(),
            review["Files"].Items.Select(changed => (changed["Path"].Text, changed["Counts"].Text)).Single()));
        Assert.Equal("Verified on attempt 2 of 2", verdict);
        Assert.StartsWith("Attempt 1 failed calculator", failed, StringComparison.Ordinal);
        Assert.Equal(("calculator.txt", "+1 -0"), file);

        await run.Ui.InvokeAsync(() => review["Files"].Items[0].Execute("ShowHunksCommand"), Cancellation);
        await run.Ui.UntilAsync(() => review["Files"].Items[0]["Hunks"].Items.Count == 1);
        Assert.Contains("+add(2, 2) = 4", await run.Ui.ReadAsync(() => review["Files"].Items[0]["Hunks"].Items[0]["Lines"].Value<IReadOnlyList<string>>()));

        await run.Ui.InvokeAsync(() => review.Execute("ApproveCommand"), Cancellation);
        await run.Ui.UntilAsync(() => review["Status"].Value<JobStatus>() == JobStatus.Approved);
        Assert.StartsWith("Approved: the branch avala/", await run.Ui.ReadAsync(() => review["Outcome"].Text), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendingAJobBackFromTheReviewSheetStartsANewRoundWithTheFeedbackAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "reply");
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var review = await ReviewAsync(run);

        await run.Ui.InvokeAsync(
            () =>
            {
                review.Set("Feedback", "Please greet the new hires too.");
                review.Execute("SendBackCommand");
            },
            Cancellation);

        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var conversation = await run.Ui.ReadAsync(() => run.Workbench()["Conversation"]);
        await run.Ui.UntilAsync(() => Prompts(conversation).Count == 2);
        Assert.Equal(["Instruction", "Sent back"], await run.Ui.ReadAsync(() => Prompts(conversation)));
        Assert.Equal("Sent back for another round", await run.Ui.ReadAsync(() => review["Outcome"].Text));
    }

    [Fact]
    public async Task AnApprovalRefusedForAConflictShowsTheConflictingFilesOnTheSheetAsync()
    {
        await using var run = await VerifiedAsync((".avala/jobs.json", ReviewTests.MergeFile));
        await run.Repository.CommitAsync("calculator.txt", "add(2, 2) = 22\n", Cancellation);
        var review = await ReviewAsync(run);

        await run.Ui.InvokeAsync(() => review.Execute("ApproveCommand"), Cancellation);

        await run.Ui.UntilAsync(() => review["Outcome"].Text.Length > 0);
        Assert.Equal(
            ("The work conflicts with the base branch in calculator.txt.", "calculator.txt", JobStatus.AwaitingReview),
            await run.Ui.ReadAsync(() => (review["Outcome"].Text, string.Join(",", review["Conflicts"].Value<IReadOnlyList<string>>()), review["Status"].Value<JobStatus>())));
    }

    [Fact]
    public async Task TheDecisionsPopoverAnswersAPermissionAndAFormOfTwoJobsAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "permission");
        var question = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("question"))));
        var decisions = run.Workbench()["Sidebar"]["Decisions"];
        await run.Ui.UntilAsync(() => decisions["Items"].Items.Count == 2);
        var kinds = await run.Ui.ReadAsync(() => decisions["Items"].Items.Select(item => item["Card"].Kind).Order(StringComparer.Ordinal).ToList());

        await run.Ui.InvokeAsync(() => decisions.Execute("AnswerCommand"), Cancellation);
        await run.Ui.UntilAsync(() => decisions["Items"].Items.Count == 1);
        await run.Ui.InvokeAsync(() => decisions.Execute("AnswerCommand"), Cancellation);

        Assert.Equal(["FormCardViewModel", "PermissionCardViewModel"], kinds);
        Assert.Equal([JobStatus.AwaitingReview, JobStatus.AwaitingReview], await run.SettledAsync(run.Job, question));
        await run.Ui.UntilAsync(() => decisions["IsEmpty"].Value<bool>());
    }

    [Fact]
    public async Task TheInspectorShowsTheEvidenceDecisionsAndUsageOfAFinishedJobAsync()
    {
        await using var run = await VerifiedAsync();
        var workbench = await SelectAsync(run);

        await run.Ui.InvokeAsync(() => workbench.Execute("ToggleInspectorCommand"), Cancellation);

        await run.Ui.UntilAsync(() => workbench.Has("Inspector") && workbench["Inspector"]["Usage"]["Spent"].Text.EndsWith("tokens", StringComparison.Ordinal));
        var inspector = await run.Ui.ReadAsync(() => workbench["Inspector"]);
        var (summary, attempts, decisions) = await run.Ui.ReadAsync(() => (
            inspector["Evidence"]["Summary"].Text,
            inspector["Evidence"]["Attempts"].Value<IReadOnlyList<string>>(),
            inspector["Audit"]["Decisions"].Value<IReadOnlyList<string>>()));
        Assert.Equal("Verified on attempt 2 of 2", summary);
        Assert.Equal(2, attempts.Count);
        Assert.StartsWith("Attempt 1: failed", attempts[0], StringComparison.Ordinal);
        Assert.Contains(decisions, decision => decision.EndsWith("· rule tests", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StoppingAJobKeepsItsWorktreeAndTheJobCanBeContinuedAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "hang");
        var workbench = await SelectAsync(run);
        var composer = await run.Ui.ReadAsync(() => workbench["Conversation"]["Composer"]);
        await run.ResumableAsync();
        await run.Ui.UntilAsync(() => GroupOf(run) == ("Running", "working"));

        await run.Ui.InvokeAsync(() => composer.Execute("StopCommand"), Cancellation);

        var hold = await run.HoldAsync();
        await run.Ui.UntilAsync(() => GroupOf(run) == ("NeedsYou", "stopped"));
        Assert.Equal((HoldReason.Stopped, SessionHalt.Stopped), (hold.Reason, hold.Halt));
        Assert.True(Directory.Exists(run.Worktree));
        await run.Ui.InvokeAsync(
            () =>
            {
                composer.Set("Draft", "Carry on where you stopped.");
                composer.Execute("SendCommand");
            },
            Cancellation);
        await run.Ui.UntilAsync(() => GroupOf(run).Group == "ReadyForReview");
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

    private static async Task<Bound> ReviewAsync(SimulatedRun run)
    {
        var workbench = await SelectAsync(run);
        await run.Ui.UntilAsync(() => ((System.Windows.Input.ICommand)workbench["OpenReviewCommand"].Target).CanExecute(null));
        await run.Ui.InvokeAsync(() => workbench.Execute("OpenReviewCommand"), Cancellation);
        await run.Ui.UntilAsync(() => workbench.Has("Review") && workbench["Review"]["IsLoaded"].Value<bool>());

        return await run.Ui.ReadAsync(() => workbench["Review"]);
    }

    private static async Task<Bound> SelectAsync(SimulatedRun run)
    {
        var workbench = run.Workbench();
        await run.Ui.UntilAsync(() => Row(workbench, run.Job) is not null);
        await run.Ui.InvokeAsync(() => workbench["Sidebar"].Execute("SelectCommand", Row(workbench, run.Job)!.Value.Target), Cancellation);

        return workbench;
    }

    private static (string Group, string Fact) GroupOf(SimulatedRun run)
    {
        var sidebar = run.Workbench()["Sidebar"];
        var group = Groups.Single(name => sidebar[name].Items.Any(row => row["Job"].Value<JobId>() == run.Job));

        return (group, Row(run.Workbench(), run.Job)!.Value["Fact"].Text);
    }

    private static Bound? Row(Bound workbench, JobId job) =>
        Groups.SelectMany(group => workbench["Sidebar"][group].Items).Cast<Bound?>().FirstOrDefault(row => row!.Value["Job"].Value<JobId>() == job);

    private static List<string> Prompts(Bound conversation) =>
        [.. conversation["Entries"].Items.Where(entry => entry.Kind == "PromptViewModel").Select(prompt => prompt["Origin"].Text)];
}
