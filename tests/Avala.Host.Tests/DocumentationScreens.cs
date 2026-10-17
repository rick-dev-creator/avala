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
