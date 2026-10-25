using Avala.Jobs.Contracts;
using Avala.Testing;
using Avala.Testing.UI;
using Avala.Verification.Contracts;
using Avalonia.Styling;

namespace Avala.Host.Tests;

public sealed class ChecksDiscoveryScreens(HeadlessUi ui, PublishedPlugins plugins)
{
    private const string ScoutBrief = "Find out how this repository proves";

    private static readonly (string Path, string Content) Autonomous = (".avala/permissions.json", """{ "autonomy": "autonomous" }""");

    private FeatureCamera Camera { get; } = new(ui, FeatureCamera.Folder ?? string.Empty);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AvalaFindsTheChecksOfARepositoryWithoutThemWhileItsFirstJobRunsAndTheReviewSaysSoAsync()
    {
        Assert.SkipUnless(FeatureCamera.Folder is not null && HeadlessApp.Renders, $"Set {FeatureCamera.Gate} to a folder and AVALA_HEADLESS_RENDER=1 to capture the feature screens.");
        await using var run = await SimulatedRun.PreparedAsync(plugins, [SimulatedRun.DiscoveryOn], [Autonomous]);
        run.Cues.Cue(ScoutBrief, "discover-checks-on-cue");
        var discoveries = run.Watch<ChecksDiscoveryChanged>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: edit] Add a greeting to the README")));
        _ = await discoveries.UntilAsync(changed => changed.Discovery.Job == job && changed.Discovery.State == DiscoveryState.Running);
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(job);
        var card = workbench.Page["Regions"]["Conversation"].Items.Single(item => item.Kind == "ReadinessCardViewModel");
        await run.Ui.RunAsync(() => card["Loading"].Value<Task>());
        await run.Ui.PresentedAsync(card.Presentation, () => card["IsShown"].Value<bool>(), () => "no readiness card");

        await Camera.ShootAsync(run, "checks-01-finding-dark");
        await Camera.ShootAsync(run, "checks-01-finding-light", ThemeVariant.Light);

        var scout = Assert.Single(await run.Get<IJobCatalog>().ChildrenAsync(job, Cancellation)).Job;
        _ = Outcomes.Succeeds(await run.Get<IJobs>().SteerAsync(scout, new JobMessage("Go on."), Cancellation));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        await run.Ui.PresentedAsync(
            card.Presentation,
            () => card["Items"].Items.Any(item => item["IsDone"].Value<bool>()),
            () => $"the card reads {string.Join(" | ", card["Items"].Items.Select(item => item["Text"].Text))}");

        await Camera.ShootAsync(run, "checks-02-found-dark");
        await Camera.ShootAsync(run, "checks-02-found-light", ThemeVariant.Light);

        _ = await workbench.InspectAsync(job, "EvidenceSectionViewModel");
        await Camera.ShootAsync(run, "checks-03-evidence-dark", FeatureCamera.Unfold("Evidence"));

        await run.Ui.RunAsync(() => workbench.Page.ExecuteAsync("OpenReviewCommand"));
        await Camera.ShootAsync(run, "checks-04-review-dark");
        await Camera.ShootAsync(run, "checks-04-review-light", ThemeVariant.Light);
    }

    [Fact]
    public async Task InASupervisedRepositoryTheCardProposesTheChecksAndOneClickUsesThemAsync()
    {
        Assert.SkipUnless(FeatureCamera.Folder is not null && HeadlessApp.Renders, $"Set {FeatureCamera.Gate} to a folder and AVALA_HEADLESS_RENDER=1 to capture the feature screens.");
        await using var run = await SimulatedRun.PreparedAsync(plugins, [SimulatedRun.DiscoveryOn], []);
        var discoveries = run.Watch<ChecksDiscoveryChanged>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: reply] Explain what the README promises")));
        _ = await discoveries.UntilAsync(changed => changed.Discovery.Job == job && changed.Discovery.State == DiscoveryState.Proposed);
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(job);
        var card = workbench.Page["Regions"]["Conversation"].Items.Single(item => item.Kind == "ReadinessCardViewModel");
        await run.Ui.RunAsync(() => card["Loading"].Value<Task>());
        await run.Ui.PresentedAsync(card.Presentation, () => card["Items"].Items.Any(item => item["Dismiss"].Text.Length > 0), () => "no proposal on the card");

        await Camera.ShootAsync(run, "checks-05-proposed-dark");
        await Camera.ShootAsync(run, "checks-05-proposed-light", ThemeVariant.Light);

        await run.Ui.RunAsync(() => card["Items"].Items[0].ExecuteAsync("ActCommand"));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        await run.Ui.PresentedAsync(card.Presentation, () => card["Items"].Items.Any(item => item["IsDone"].Value<bool>()), () => "the proposal was not used");

        await Camera.ShootAsync(run, "checks-06-used-dark");
    }
}
