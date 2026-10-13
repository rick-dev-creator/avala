using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;

namespace Avala.Host.Tests;

public sealed class ProjectsTests(PublishedPlugins plugins)
{
    [Fact]
    public async Task AJobRunsUnderItsRepositorysProjectAndMovesFromItsMenuIntoANewProjectRenamedInPlaceAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "reply");
        var workbench = await OpenWorkbench.ActivateAsync(run);
        await run.SettledAsync();
        var folder = Path.GetFileName(Path.TrimEndingDirectorySeparator(run.Repository.Path));
        await workbench.ShowsAsync(() => ProjectOf(workbench.Sidebar, run.Job) == (folder, true));

        await run.Ui.RunAsync(() => Row(workbench.Sidebar, run.Job)["MoveTargets"].Items.Single(target => target["Name"].Text == "New project…").ExecuteAsync("Move"));
        await workbench.ShowsAsync(() => ProjectOf(workbench.Sidebar, run.Job) == ("New project", true) && Group(workbench.Sidebar, "New project")["IsRenaming"].Value<bool>());
        await run.Ui.RunAsync(() =>
        {
            var created = Group(workbench.Sidebar, "New project");
            created.Set("Draft", "Experiments");

            return created.ExecuteAsync("CommitRenameCommand");
        });
        await workbench.ShowsAsync(() => ProjectOf(workbench.Sidebar, run.Job) == ("Experiments", true));

        var kept = run.Get<IProjects>().Current;
        Assert.Equal(["Experiments", folder], kept.Projects.Select(project => project.Name).Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal("Experiments", kept.Of(run.Job, run.Repository.Path).Match(project => project.Name, () => string.Empty));
        Assert.Equal(["Experiments", folder], await run.Ui.ReadAsync(() => workbench.Sidebar["Projects"].Items.Select(group => group["Name"].Text).ToList()));
    }

    private static Bound Group(Bound sidebar, string name) =>
        sidebar["Projects"].Items.Single(group => group["Name"].Text == name);

    private static Bound Row(Bound sidebar, JobId job) =>
        SidebarRows.TopLevel(sidebar).Single(row => row["Job"].Value<JobId>() == job);

    private static (string Name, bool IsProject) ProjectOf(Bound sidebar, JobId job) =>
        sidebar["Projects"].Items
            .Where(group => group["Open"].Items.Concat(group["Done"].Items).Any(row => row["Job"].Value<JobId>() == job))
            .Select(group => (group["Name"].Text, group["IsProject"].Value<bool>()))
            .FirstOrDefault((string.Empty, false));
}
