using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class ArchiveTests(PublishedPlugins plugins)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AConversationArchivedFromItsRowLeavesTheSidebarForTheArchivedPageAndIsRestoredFromItAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "reply");
        var workbench = await ArchivedAsync(run);
        var entry = workbench.Footer("ArchivedEntryViewModel");
        await run.Ui.PresentedAsync(entry.Presentation, () => entry["Count"].Text == "1", () => entry["Count"].Text);

        var page = run.Page("Archived");
        await run.Ui.InvokeAsync(() => entry.Execute("OpenCommand"), Cancellation);
        await run.Ui.InvokeAsync(((IActivatable)page.Value<object>()).Activate, Cancellation);
        await run.Ui.PresentedAsync(page.Presentation, () => page["HasArchived"].Value<bool>(), () => page["Summary"].Text);
        var row = await run.Ui.ReadAsync(() => page["Groups"].Items.Single()["Rows"].Items.Single());
        await run.Ui.RunAsync(() => row.ExecuteAsync("RestoreCommand"));

        await workbench.ShowsAsync(() => Listed(workbench, run.Job));
        await run.Ui.PresentedAsync(entry.Presentation, () => !entry["HasArchived"].Value<bool>(), () => entry["Count"].Text);
        Assert.Empty(run.Get<IProjects>().Current.Archived);
    }

    [Fact]
    public async Task AnArchivedConversationThatIsSentAMessageReturnsToItsProjectAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "reply");
        var workbench = await ArchivedAsync(run);

        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(run.Job, "Add a test for it as well.", Cancellation));

        await workbench.ShowsAsync(() => Listed(workbench, run.Job));
        Assert.DoesNotContain(run.Job, run.Get<IProjects>().Current.Archived);
    }

    [Fact]
    public async Task AnArchivedConversationStaysArchivedAfterARestartAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "reply");
        await ArchivedAsync(run);

        await run.RestartAsync();
        var workbench = await OpenWorkbench.ActivateAsync(run);
        var entry = workbench.Footer("ArchivedEntryViewModel");
        await run.Ui.PresentedAsync(entry.Presentation, () => entry["Count"].Text == "1", () => entry["Count"].Text);

        Assert.Contains(run.Job, run.Get<IProjects>().Current.Archived);
        Assert.False(await run.Ui.ReadAsync(() => Listed(workbench, run.Job)));
    }

    private static async Task<OpenWorkbench> ArchivedAsync(SimulatedRun run)
    {
        var workbench = await OpenWorkbench.ActivateAsync(run);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await workbench.ShowsAsync(() => Listed(workbench, run.Job) && Row(workbench, run.Job)["CanArchive"].Value<bool>());

        await run.Ui.RunAsync(() => Row(workbench, run.Job).ExecuteAsync("ArchiveCommand"));
        await workbench.ShowsAsync(() => !Listed(workbench, run.Job));

        return workbench;
    }

    private static bool Listed(OpenWorkbench workbench, JobId job) =>
        SidebarRows.TopLevel(workbench.Sidebar).Any(row => row["Job"].Value<JobId>() == job);

    private static Bound Row(OpenWorkbench workbench, JobId job) =>
        SidebarRows.TopLevel(workbench.Sidebar).Single(row => row["Job"].Value<JobId>() == job);
}
