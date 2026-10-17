using System.Windows.Input;
using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk.Regions;
using Avala.Testing;
using Avala.Testing.UI;
using Avalonia.Styling;

namespace Avala.Host.Tests;

public sealed class DocumentationScreens(HeadlessUi ui, PublishedPlugins plugins)
{
    private const string Feedback = "Stripe sends a refund twice; apply it once.";

    private FeatureCamera Camera { get; } = new(ui, FeatureCamera.Folder ?? string.Empty);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheFeatureChipTheConversationsDocsLinesTheDocsPaneAndASendBacksChangeRequestInTheRunningApplicationAsync()
    {
        Assert.SkipUnless(FeatureCamera.Folder is not null && HeadlessApp.Renders, $"Set {FeatureCamera.Gate} to a folder and AVALA_HEADLESS_RENDER=1 to capture the feature screens.");
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", """{ "docs": { "enabled": true } }"""));
        var written = run.Watch<VersionWritten>();
        var workbench = await run.WorkbenchAsync();
        var page = run.Page("New job");
        await run.Ui.RunAsync(() =>
        {
            ((ICommand)workbench.Toolbar["NewJobCommand"].Target).Execute(null);

            return page["Loading"].Value<Task>();
        });
        await run.Ui.RunAsync(async () =>
        {
            page.Set("Repository", run.Repository.Path);
            await page["Previewing"].Value<Task>();
            await page["OptionsRead"].Value<Task>();
            await page["FeaturesRead"].Value<Task>();
            page.Set("Instruction", "[simulate: plan-revised] Handle Stripe refund webhooks");
        });
        Assert.Equal("Handle Stripe refund webhooks", await run.Ui.ReadAsync(() => page["Feature"]["Name"].Text));

        await Camera.ShootAsync(run, "docs-app-01-new-job-chip-dark");
        await Camera.ShootAsync(run, "docs-app-01-new-job-chip-light", ThemeVariant.Light);

        await run.Ui.RunAsync(() => page.ExecuteAsync("SubmitCommand"));
        var job = (await written.UntilAsync(version => version.Version.Kind == VersionKind.AsBuilt)).Job;
        var conversation = await workbench.SelectAsync(job);
        await workbench.ShowsAsync(() => OpenWorkbench.Kinds(conversation).Count(kind => kind == "DocsLineViewModel") == 2);

        await Camera.ShootAsync(run, "docs-app-02-conversation-docs-lines-dark");

        var pane = await OpenPaneAsync(run, "2 versions");

        await Camera.ShootAsync(run, "docs-app-03-docs-pane-dark");
        await Camera.ShootAsync(run, "docs-app-03-docs-pane-light", ThemeVariant.Light);

        Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, Feedback, Cancellation));
        _ = await written.UntilAsync(version => version.Job == job && version.Version.Kind == VersionKind.AsBuilt);
        await workbench.ShowsAsync(() => OpenWorkbench.Kinds(conversation).Contains("ChangeLineViewModel"));
        await run.Ui.PresentedAsync(pane.Presentation, () => pane["State"].Text == "4 versions", () => $"the pane says {pane["State"].Text}");

        await Camera.ShootAsync(run, "docs-app-04-change-request-dark");
        await Camera.ShootAsync(run, "docs-app-04-change-request-light", ThemeVariant.Light);

        await run.Ui.InvokeAsync(() => pane["History"]["Steps"].Items.Single(step => step["Title"].Text == "Plan v3 · deviation").Execute("ChooseCommand"), Cancellation);
        Assert.True(await run.Ui.ReadAsync(() => pane["History"]["HasWriter"].Value<bool>()));

        await Camera.ShootAsync(run, "docs-app-05-writer-link-dark", window => FeatureCamera.ScrollTo(window, "Writer's conversation"));
    }

    [Fact]
    public async Task TheHistoryTheFeaturesPageAndTheDocsSiteOfAnApprovedJobAsync()
    {
        Assert.SkipUnless(FeatureCamera.Folder is not null && HeadlessApp.Renders, $"Set {FeatureCamera.Gate} to a folder and AVALA_HEADLESS_RENDER=1 to capture the feature screens.");
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", """{ "docs": { "enabled": true }, "approval": "merge" }"""));
        var written = run.Watch<VersionWritten>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: plan-revised] Handle Stripe refund webhooks")));
        _ = await written.UntilAsync(version => version.Job == job && version.Version.Kind == VersionKind.AsBuilt);
        Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, Feedback, Cancellation));
        _ = await written.UntilAsync(version => version.Job == job && version.Version.Kind == VersionKind.AsBuilt);
        var progressed = run.Watch<JobProgressed>();
        Outcomes.Succeeds(await run.Get<IJobs>().ApproveAsync(job, Cancellation));
        _ = await progressed.UntilAsync(update => update.Job == job && update.Status == JobStatus.Approved);

        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(job);
        var pane = await OpenPaneAsync(run, "4 versions");
        await run.Ui.InvokeAsync(() => pane["History"]["Steps"].Items.Single(step => step["Title"].Text == "Plan v3 · deviation").Execute("ChooseCommand"), Cancellation);

        await Camera.ShootAsync(run, "docs-history-dark", window => FeatureCamera.ScrollTo(window, "History"));
        await Camera.ShootAsync(run, "docs-history-light", ThemeVariant.Light, window => FeatureCamera.ScrollTo(window, "History"));

        var group = workbench.Sidebar["Projects"].Items.Single();
        await run.Ui.RunAsync(() => group["DocsRead"].Value<Task>());
        var docs = group["Docs"];
        await run.Ui.PresentedAsync(workbench.Sidebar.Presentation, () => docs["Features"].Items.Count == 1, () => $"{docs["Features"].Items.Count} features in the sidebar");
        await run.Ui.InvokeAsync(() => docs.Execute("ToggleCommand"), Cancellation);
        await run.Ui.InvokeAsync(() => docs["Features"].Items[0].Execute("OpenCommand"), Cancellation);
        var page = run.Page("Docs");
        await run.Ui.RunAsync(() => page["Loading"].Value<Task>());
        Assert.True(await run.Ui.ReadAsync(() => page["Player"]["IsDrawn"].Value<bool>()));

        await Camera.ShootAsync(run, "docs-feature-page-dark");
        await Camera.ShootAsync(run, "docs-feature-page-light", ThemeVariant.Light);

        await run.Ui.InvokeAsync(() => page["Player"].Execute("NextCommand"), Cancellation);
        await run.Ui.InvokeAsync(() => page["Player"].Execute("NextCommand"), Cancellation);
        await Camera.ShootAsync(run, "docs-feature-page-step-dark");

        var site = Outcomes.Succeeds(await run.Get<IDocumentation>().CreateSiteAsync(run.Repository.Path, Cancellation));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(Outcomes.Present(site.Filler)));
        Copy(Path.Combine(run.Repository.Path, site.Root), Path.Combine(FeatureCamera.Folder, "site"));
    }

    private static void Copy(string from, string to)
    {
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static async Task<Bound> OpenPaneAsync(SimulatedRun run, string state)
    {
        var contributions = run.Get<IEnumerable<RegionContribution>>().ToList();
        var toggle = new Bound(contributions.Single(contribution => contribution.Region == WorkbenchRegions.JobHeader && contribution.ViewModel.GetType().Name == "DocsToggleViewModel").ViewModel);
        var pane = new Bound(contributions.Single(contribution => contribution.Region == WorkbenchRegions.Preview && contribution.ViewModel.GetType().Name == "DocsPaneViewModel").ViewModel);
        await run.Ui.PresentedAsync(toggle.Presentation, () => toggle["IsShown"].Value<bool>(), () => "the docs glyph is not shown");
        await run.Ui.InvokeAsync(() => toggle.Execute("ToggleCommand"), Cancellation);
        await run.Ui.PresentedAsync(pane.Presentation, () => pane["IsOpen"].Value<bool>() && pane["State"].Text == state, () => $"the pane says {pane["State"].Text}");

        return pane;
    }
}
