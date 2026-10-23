using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Testing.UI;

namespace Avala.Host.Tests;

[UsesRealGit]
public sealed class DeliveryFlowScreens(HeadlessUi ui, PublishedPlugins plugins)
{
    private const string GhStatus = """
        github.com
          ✓ Logged in to github.com account octocat (keyring)
          - Active account: true
        """;

    private static readonly (string File, string Content)[] LocalForge =
    [
        ("forges.json", """{ "pollSeconds": 60, "forges": [{ "name": "local", "forge": "simulated" }] }"""),
    ];

    private static readonly Dictionary<string, ProcessOutcome> SignedIn = new(StringComparer.Ordinal) { ["gh"] = new ProcessOutcome(0, GhStatus, string.Empty) };

    private FeatureCamera Camera { get; } = new(ui, FeatureCamera.Folder ?? string.Empty);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARepositorysFirstJobOpensWithWhatItStillNeedsAndConnectingTurnsItIntoACheckMarkAsync()
    {
        Gated();
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], [], signIns: SignedIn);
        await run.Repository.GitAsync(Cancellation, "remote", "add", "origin", "https://github.com/octocat/shop.git");
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("reply"))));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(job);
        var card = workbench.Page["Regions"]["Conversation"].Items.Single(item => item.Kind == "ReadinessCardViewModel");
        await run.Ui.RunAsync(() => card["Loading"].Value<Task>());
        await run.Ui.PresentedAsync(card.Presentation, () => card["IsShown"].Value<bool>(), () => "no readiness card");

        await Camera.ShootAsync(run, "beta11-readiness-card");

        await run.Ui.RunAsync(() => card["Items"].Items[0].ExecuteAsync("ActCommand"));
        await Camera.ShootAsync(run, "beta11-readiness-connected");
    }

    [Fact]
    public async Task TheReviewNamesWhatApprovingDoesAndADeliveredJobStaysAmongTheOpenJobsAsync()
    {
        Gated();
        await using var run = await SimulatedRun.InstructedAsync(plugins, SimulatedRun.Simulate("reply"), LocalForge, []);
        _ = await run.Repository.PublishAsync(Cancellation);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() => ((System.Windows.Input.ICommand)workbench.Page["OpenReviewCommand"].Target).CanExecute(null));
        await run.Ui.RunAsync(() => workbench.Page.ExecuteAsync("OpenReviewCommand"));

        await Camera.ShootAsync(run, "beta11-review-named-action");

        var review = await run.Ui.ReadAsync(() => workbench.Page["Review"]);
        await run.Ui.RunAsync(() => review.ExecuteAsync("OpenPullRequestCommand"));
        _ = await run.Get<IPullRequests>().RefreshAsync(run.Job, Cancellation);
        var number = Outcomes.Present(run.Get<IPullRequests>().Of(run.Job)).PullRequest.Number;
        await workbench.ShowsInGroupAsync(run.Job, "Delivered", $"delivered · #{number} · checks 1 of 1");
        await run.Ui.InvokeAsync(() => review.Execute("CloseCommand"), Cancellation);

        await Camera.ShootAsync(run, "beta11-delivered-job");
    }

    [Fact]
    public async Task WithoutAConnectionThePullRequestTheAgentReportsIsNamedAboveItsConversationAsync()
    {
        Gated();
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/checks.json", """{ "checks": [{ "name": "git", "command": "git", "arguments": ["--version"] }] }"""));
        _ = await run.Repository.PublishAsync(Cancellation);
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("mentions"))));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(job);
        var note = workbench.Page["Regions"]["Conversation"].Items.Single(item => item.Kind == "AgentPullRequestNoteViewModel");
        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, "Opened https://forge.invalid/octo/shop/pulls/1", Cancellation));
        await run.Ui.PresentedAsync(note.Presentation, () => note["IsShown"].Value<bool>(), () => "no note of the agent's pull request");

        await Camera.ShootAsync(run, "beta11-agent-pull-request-note");
    }

    [Fact]
    public async Task ThePullRequestTheAgentOpenedFromABranchItNeverCheckedOutShowsInTheInspectorAsync()
    {
        Gated();
        await using var run = await SimulatedRun.PreparedAsync(plugins, LocalForge, []);
        _ = await run.Repository.PublishAsync(Cancellation);
        var (job, worktree) = await ForgeTests.ReviewedEditAsync(run, "mentions");
        var number = await ForgeTests.PullRequestFromABranchNeverCheckedOutAsync(run, worktree, "fix/rounding");
        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, $"Opened {ForgeTests.PullRequestLink(number)}", Cancellation));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        var workbench = await run.WorkbenchAsync();
        var section = (await workbench.InspectAsync(job, "PullRequestSectionViewModel"))[0];
        await run.Ui.PresentedAsync(section.Presentation, () => section["Fact"].Text.Contains($"#{number}", StringComparison.Ordinal), () => $"the inspector reads {section["Fact"].Text}");

        await Camera.ShootAsync(run, "stabilize-agent-pull-request-inspector", FeatureCamera.Unfold("Pull request"));
    }

    [Fact]
    public async Task AJobWithoutChecksShowsWhatTheAgentRanAfterItsLastEditAndOffersToDeclareChecksAsync()
    {
        Gated();
        await using var run = await SimulatedRun.InstructedAsync(plugins, SimulatedRun.Simulate("edit"), [], [(".avala/permissions.json", """{ "autonomy": "autonomous" }""")]);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.InspectAsync(run.Job, "EvidenceSectionViewModel");

        await Camera.ShootAsync(run, "stabilize-no-checks-evidence", FeatureCamera.Unfold("Evidence"));

        await run.Ui.RunAsync(() => workbench.Page.ExecuteAsync("OpenReviewCommand"));
        await Camera.ShootAsync(run, "stabilize-no-checks-review");
    }

    [Fact]
    public async Task AnApprovedJobsConversationStillTakesAMessageAsync()
    {
        Gated();
        await using var run = await SimulatedRun.StartAsync(plugins, "reply");
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() => ((System.Windows.Input.ICommand)workbench.Page["OpenReviewCommand"].Target).CanExecute(null));
        await run.Ui.RunAsync(() => workbench.Page.ExecuteAsync("OpenReviewCommand"));
        var review = await run.Ui.ReadAsync(() => workbench.Page["Review"]);
        await run.Ui.RunAsync(() => review.ExecuteAsync("ApproveCommand"));
        await workbench.ShowsAsync(() => review["Status"].Value<JobStatus>() == JobStatus.Approved);
        await run.Ui.InvokeAsync(() => review.Execute("CloseCommand"), Cancellation);
        var conversation = await run.Ui.ReadAsync(() => workbench.Page["Conversation"]);
        await run.Ui.InvokeAsync(() => conversation["Composer"].Set("Draft", "Please greet the new hires too."), Cancellation);

        await Camera.ShootAsync(run, "stabilization-approved-job-composer");
    }

    private static void Gated() =>
        Assert.SkipUnless(FeatureCamera.Folder is not null && HeadlessApp.Renders, $"Set {FeatureCamera.Gate} to a folder and AVALA_HEADLESS_RENDER=1 to capture the feature screens.");
}
