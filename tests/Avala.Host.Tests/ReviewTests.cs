using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Supervision.Contracts;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Host.Tests;

public sealed class ReviewTests(PublishedPlugins plugins)
{
    private const string Fixed = "add(2, 2) = 4\n";

    internal const string MergeFile = """{ "approval": "merge" }""";

    internal const string TestsPolicy = """{ "rules": [ { "name": "tests", "kind": "command", "target": "dotnet test*", "answer": "allow" } ] }""";

    internal const string CalculatorChecks = """
        {
          "checks": [
            { "name": "calculator", "command": "git", "arguments": ["grep", "--quiet", "--fixed-strings", "add(2, 2) = 4", "--", "calculator.txt"], "timeoutSeconds": 60 }
          ]
        }
        """;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AVerifiedJobApprovedWithKeepLeavesItsBranchReadyAndTouchesNothingElseAsync()
    {
        await using var run = await VerifiedAsync();
        var workspace = await WorkspaceAsync(run);
        var main = await run.Repository.GitAsync(Cancellation, "rev-parse", "main");

        var approval = Outcomes.Succeeds(await run.Get<IJobs>().ApproveAsync(run.Job, Cancellation));

        Assert.Equal(new ApprovalDelivery("keep", workspace.Branch, Option<string>.None), approval.Delivery);
        Assert.Equal(JobStatus.Approved, await StatusAsync(run));
        Assert.Equal(main, await run.Repository.GitAsync(Cancellation, "rev-parse", "main"));
        Assert.Equal(Fixed.Trim(), await run.Repository.GitAsync(Cancellation, "show", $"{workspace.Branch}:calculator.txt"));
        Assert.True(Directory.Exists(workspace.Path));
    }

    [Fact]
    public async Task AVerifiedJobApprovedWithMergeLandsOnTheBaseBranchAsOneCommitAsync()
    {
        await using var run = await VerifiedAsync((".avala/jobs.json", MergeFile));
        var workspace = await WorkspaceAsync(run);
        var before = await run.Repository.GitAsync(Cancellation, "rev-parse", "main");
        var diff = Outcomes.Succeeds(await run.Get<IWorkspaceChanges>().DiffAsync(workspace.Id, Cancellation));
        Assert.Equal([new FileChange("calculator.txt", ChangeKind.Added, 1, 0)], diff.Files);

        var approval = Outcomes.Succeeds(await run.Get<IJobs>().ApproveAsync(run.Job, Cancellation));

        var commit = Outcomes.Present(approval.Delivery.Commit);
        Assert.Equal(("merge", "main"), (approval.Delivery.Strategy, approval.Delivery.Branch));
        Assert.Equal(commit, await run.Repository.GitAsync(Cancellation, "rev-parse", "main"));
        Assert.Equal(before, await run.Repository.GitAsync(Cancellation, "rev-parse", $"{commit}^"));
        Assert.Equal(SimulatedRun.Simulate("fix-after-feedback"), await run.Repository.GitAsync(Cancellation, "log", "-1", "--format=%s", commit));
        Assert.Equal(Fixed, (await File.ReadAllTextAsync(Path.Combine(run.Repository.Path, "calculator.txt"), Cancellation)).ReplaceLineEndings("\n"));
        Assert.Empty(await run.Repository.GitAsync(Cancellation, "status", "--porcelain"));
        Assert.Equal(JobStatus.Approved, await StatusAsync(run));
    }

    [Fact]
    public async Task ABaseThatMovedIntoAConflictIsReportedAndTheJobStaysAwaitingReviewAsync()
    {
        await using var run = await VerifiedAsync((".avala/jobs.json", MergeFile));
        var workspace = await WorkspaceAsync(run);
        await run.Repository.CommitAsync("calculator.txt", "add(2, 2) = 22\n", Cancellation);
        var tip = await run.Repository.GitAsync(Cancellation, "rev-parse", "main");

        Assert.Equal(JobRejection.MergeConflict, Outcomes.FailsWith(await run.Get<IJobs>().ApproveAsync(run.Job, Cancellation)));

        Assert.Equal(["calculator.txt"], Outcomes.Succeeds(await run.Get<IWorkspaceChanges>().ConflictsAsync(workspace.Id, Cancellation)));
        Assert.Equal(tip, await run.Repository.GitAsync(Cancellation, "rev-parse", "main"));
        Assert.Equal(JobStatus.AwaitingReview, await StatusAsync(run));
    }

    [Fact]
    public async Task ABaseCheckoutWithUncommittedChangesIsRefusedAndLeftAsItWasAsync()
    {
        await using var run = await VerifiedAsync((".avala/jobs.json", MergeFile));
        var tip = await run.Repository.GitAsync(Cancellation, "rev-parse", "main");
        const string Notes = "# Shop\n\nNotes I have not committed.\n";
        await File.WriteAllTextAsync(Path.Combine(run.Repository.Path, "README.md"), Notes, Cancellation);

        Assert.Equal(JobRejection.BaseCheckoutDirty, Outcomes.FailsWith(await run.Get<IJobs>().ApproveAsync(run.Job, Cancellation)));

        Assert.Equal(Notes, await File.ReadAllTextAsync(Path.Combine(run.Repository.Path, "README.md"), Cancellation));
        Assert.False(File.Exists(Path.Combine(run.Repository.Path, "calculator.txt")));
        Assert.Equal(tip, await run.Repository.GitAsync(Cancellation, "rev-parse", "main"));
        Assert.Equal(JobStatus.AwaitingReview, await StatusAsync(run));
    }

    [Fact]
    public async Task AJobSentBackStartsANewRoundWithTheFeedbackAsync()
    {
        const string Feedback = "Please greet the new hires too.";
        await using var run = await SimulatedRun.StartAsync(plugins, "reply");
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());

        var continued = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(run.Job, Feedback, Cancellation));

        Assert.Equal(ContinuedIn.SameSession, continued.Conversation);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var history = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(run.Job, Cancellation));
        Assert.Equal(
            [(AttemptOrigin.Initial.ToString(), "Passed", ""), (AttemptOrigin.SendBack.ToString(), "Passed", Feedback)],
            history.Attempts.Select(attempt => (attempt.Origin.ToString(), attempt.Outcome.ToString(), attempt.Guidance.Match(text => text, () => string.Empty))));
        Assert.Equal([continued.Session], history.Sessions.Select(session => session.Session));
    }

    [Fact]
    public async Task UsageAndInterventionsSurviveARestartAndUsageIsReadByTimeWindowAsync()
    {
        var started = DateTimeOffset.UtcNow.AddMinutes(-1);
        await using var run = await SimulatedRun.InstructedAsync(
            plugins,
            SimulatedRun.Simulate("hang"),
            [("supervision.json", """{ "silenceSeconds": 1 }""")],
            [(".avala/budget.json", """{ "costPerJob": { "USD": 0.01 } }""")]);
        var stalled = await run.SupervisorInterventionAsync();
        var spending = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("permission"))));
        var exceeded = await run.BudgetInterventionAsync();
        Assert.Equal(spending, exceeded.Hold.Job);
        Assert.Equal([JobStatus.NeedsHelp], await run.SettledAsync(spending));
        var spent = Outcomes.Present(run.Get<IUsage>().OfJob(spending));

        await run.RestartAsync();
        await run.StartedAsync();

        Assert.Equal([stalled], run.Get<ISupervision>().OfJob(run.Job));
        Assert.Equal([exceeded], run.Get<IBudgets>().OfJob(spending));
        var restored = Outcomes.Present(run.Get<IUsage>().OfJob(spending));
        Assert.Equal(spent.Tokens, restored.Tokens);
        Assert.Equal(spent.Costs, restored.Costs);
        var history = run.Get<IUsageHistory>();
        var now = DateTimeOffset.UtcNow.AddMinutes(1);
        var window = await history.WithinAsync(started, now, Cancellation);
        Assert.Equal((spent.Tokens, spent.Costs[0]), (window.Usage.Tokens, Assert.Single(window.Usage.Costs)));
        Assert.Equal(0, (await history.WithinAsync(started.AddDays(-1), started, Cancellation)).Usage.Tokens.Input);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var days = await history.DailyAsync(today.AddDays(-1), today, TimeZoneInfo.Utc, Cancellation);
        Assert.Equal(spent.Tokens.Input, days.Sum(day => day.Usage.Tokens.Input));
    }

    private Task<SimulatedRun> VerifiedAsync(params (string Path, string Content)[] committed) =>
        SettledAsync(SimulatedRun.StartAsync(
            plugins,
            "fix-after-feedback",
            [(".avala/checks.json", CalculatorChecks), (".avala/permissions.json", TestsPolicy), .. committed]));

    private static async Task<SimulatedRun> SettledAsync(Task<SimulatedRun> starting)
    {
        var run = await starting;
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());

        return run;
    }

    private static async Task<WorkspaceInfo> WorkspaceAsync(SimulatedRun run)
    {
        var history = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(run.Job, Cancellation));

        return Outcomes.Succeeds(await run.Get<IWorkspaces>().FindAsync(Outcomes.Present(history.Summary.Workspace), Cancellation));
    }

    private static async Task<JobStatus> StatusAsync(SimulatedRun run) =>
        Assert.Single(await run.Get<IJobCatalog>().ListAsync(Cancellation), summary => summary.Job == run.Job).Status;
}
