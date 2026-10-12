using System.Windows.Input;
using Avala.Jobs.Contracts;
using Avala.Testing;
using Avala.Testing.UI;
using Avalonia.Styling;

namespace Avala.Host.Tests;

public sealed class RepositoryScreens(HeadlessUi ui, PublishedPlugins plugins)
{
    private static readonly (string File, string Content)[] LocalForge =
    [
        ("forges.json", """{ "pollSeconds": 60, "forges": [{ "name": "local", "forge": "simulated" }] }"""),
    ];

    private FeatureCamera Camera { get; } = new(ui, FeatureCamera.Folder ?? string.Empty);

    [Fact]
    public async Task TheNewJobRepositoryFieldClonesAnAddressAndPublishesANewRepositoryAsync()
    {
        Gated();
        await using var projects = new TemporaryFolder();
        await using var run = await SimulatedRun.PreparedAsync(plugins, LocalForge, []);
        _ = Outcomes.Succeeds(await run.Get<Avala.Workspaces.Contracts.IProjectsFolder>().ChooseAsync(projects.Path, TestContext.Current.CancellationToken));
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
            await page["Chooser"]["Checking"].Value<Task>();
            var cloning = page["Chooser"]["Clone"];
            await cloning.ExecuteAsync("StartCommand");
            cloning.Set("Search", "https://github.com/octo/shop.git");
            cloning.Set("IsCloning", true);
            cloning.Set("Status", "Cloning shop…");
        });
        await Camera.ShootAsync(run, "43-new-job-clone-running-dark");
        await Camera.ShootAsync(run, "43-new-job-clone-running-light", ThemeVariant.Light);

        await run.Ui.RunAsync(async () =>
        {
            var cloning = page["Chooser"]["Clone"];
            cloning.Set("IsCloning", false);
            ((ICommand)cloning["CancelCommand"].Target).Execute(null);
            var creating = page["Chooser"]["Creating"];
            creating.Set("Parent", Path.GetDirectoryName(run.Repository.Path)!);
            creating.Set("Name", "invoices");
            creating.Set("IsOpen", true);
            creating.Set("IsPublishing", true);
            await creating["Publishing"]["Loading"].Value<Task>();
        });
        await Camera.ShootAsync(run, "44-new-job-new-repository-publish-dark");
        await Camera.ShootAsync(run, "44-new-job-new-repository-publish-light", ThemeVariant.Light);
    }

    [Fact]
    public async Task TheInspectorPublishesTheRepositoryOfAJobWithoutARemoteAsync()
    {
        Gated();
        await using var run = await SimulatedRun.PreparedAsync(plugins, LocalForge, [(".avala/permissions.json", """{ "autonomy": "autonomous" }""")]);
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("edit"))));
        _ = await run.SettledAsync(job);
        var workbench = await run.WorkbenchAsync();
        var publisher = (await workbench.InspectAsync(job, "PullRequestSectionViewModel"))[0]["Publisher"];
        await run.Ui.RunAsync(async () =>
        {
            await publisher["Reading"].Value<Task>();
            await publisher.ExecuteAsync("StartCommand");
        });
        await Camera.ShootAsync(run, "45-inspector-publish-repository-dark", FeatureCamera.Unfold("Pull request"));
        await Camera.ShootAsync(run, "45-inspector-publish-repository-light", ThemeVariant.Light, FeatureCamera.Unfold("Pull request"));

        await run.Ui.RunAsync(() => publisher.ExecuteAsync("PublishCommand"));
        await Camera.ShootAsync(run, "46-inspector-published-origin-known-dark", FeatureCamera.Unfold("Pull request"));
        await Camera.ShootAsync(run, "46-inspector-published-origin-known-light", ThemeVariant.Light, FeatureCamera.Unfold("Pull request"));
    }

    private static void Gated() =>
        Assert.SkipUnless(FeatureCamera.Folder is not null && HeadlessApp.Renders, $"Set {FeatureCamera.Gate} to a folder and AVALA_HEADLESS_RENDER=1 to capture the feature screens.");
}
