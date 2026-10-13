using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Shell;
using Avala.Testing;
using Avala.Testing.UI;
using Avala.Workspaces.Contracts;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Tests;

public sealed class WelcomeScreens(HeadlessUi ui, PublishedPlugins plugins)
{
    private const string GhStatus = """
        github.com
          ✓ Logged in to github.com account octocat (keyring)
          - Active account: true
        """;

    private static readonly (string File, string Content)[] Forges =
    [
        ("forges.json", """{ "pollSeconds": 60, "forges": [{ "name": "local", "forge": "simulated" }, { "name": "work", "forge": "github", "credential": { "source": "env", "reference": "AVALA_SCREENS_UNSET_TOKEN" } }] }"""),
    ];

    private static readonly Dictionary<string, ProcessOutcome> SignedIn = new(StringComparer.Ordinal) { ["gh"] = new ProcessOutcome(0, GhStatus, string.Empty) };

    private FeatureCamera Camera { get; } = new(ui, FeatureCamera.Folder ?? string.Empty);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheWelcomeOnAnEmptyBoardAndWithoutAHarnessAsync()
    {
        Gated();
        await using (var run = await SimulatedRun.PreparedAsync(plugins, Forges, []))
        {
            var workbench = await WelcomedAsync(run);
            await ShootBothAsync(run, "60-welcome");

            await run.Ui.RunAsync(() =>
            {
                workbench.Page["Welcome"]["Harness"].Set("IsOpen", true);

                return Task.CompletedTask;
            });
            await ShootBothAsync(run, "69-welcome-harness-menu");

            var harness = workbench.Page["Welcome"]["Harness"];
            await run.Ui.RunAsync(() =>
            {
                ((System.Windows.Input.ICommand)harness["Options"].Items[1]["ChooseCommand"].Target).Execute(null);

                return workbench.Page["Welcome"]["Driver"]["Offering"].Value<Task>();
            });
            await ShootBothAsync(run, "70-welcome-harness-chosen");
        }

        await using var bare = await SimulatedRun.PreparedAsync(plugins, [], [], developer: false);
        await WelcomedAsync(bare);
        await ShootBothAsync(bare, "61-welcome-no-harness");
    }

    [Fact]
    public async Task TheWholeWelcomeWithJobsOnTheBoardAndTheNewJobRepositoryMenuAsync()
    {
        Gated();
        await using var run = await SimulatedRun.PreparedAsync(plugins, Forges, []);
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(run.Repository.Path, SimulatedRun.Simulate("reply"))));
        _ = await run.SettledAsync(job);
        var workbench = await WelcomedAsync(run);
        await ShootBothAsync(run, "62-welcome-with-jobs");

        var page = run.Page("New job");
        await run.Ui.RunAsync(() =>
        {
            ((System.Windows.Input.ICommand)workbench.Toolbar["NewJobCommand"].Target).Execute(null);

            return page["Loading"].Value<Task>();
        });
        await run.Ui.RunAsync(() =>
        {
            page["Repositories"].Set("IsOpen", true);

            return Task.CompletedTask;
        });
        await ShootBothAsync(run, "63-new-job-repository-menu");
    }

    [Fact]
    public async Task SettingsAccountsWithDetectedAndAddedAccountsAndTheConnectSheetWithItsTokenFieldAsync()
    {
        Gated();
        await using var run = await SimulatedRun.PreparedAsync(plugins, Forges, [], signIns: SignedIn);
        await run.WorkbenchAsync();
        var settings = run.Page("Settings");
        await run.Ui.RunAsync(() =>
        {
            run.Get<ShellViewModel>().SelectedPage = (IPage)settings.Target;

            return settings["Loading"].Value<Task>();
        });
        await ShootBothAsync(run, "64-settings-accounts");

        var sourceControl = settings["Machine"]["Accounts"]["SourceControl"];
        await run.Ui.RunAsync(() => sourceControl.ExecuteAsync("ConnectCommand"));
        await ShootBothAsync(run, "65-connect-sheet");

        await run.Ui.RunAsync(() =>
        {
            sourceControl["Sheet"].Set("UsesToken", true);

            return Task.CompletedTask;
        });
        await ShootBothAsync(run, "68-connect-sheet-token");
    }

    [Fact]
    public async Task TheCloneSheetWithTheRepositoryListAndWithAPastedAddressAsync()
    {
        Gated();
        await using var projects = new TemporaryFolder();
        await using var run = await SimulatedRun.PreparedAsync(plugins, Forges, []);
        await PublishedAsync(run, run.Repository, "shop");
        await using var ledger = await TemporaryRepository.CreateAsync(run.Get<IProcessRunner>(), Cancellation);
        await PublishedAsync(run, ledger, "ledger");
        await using var design = await TemporaryRepository.CreateAsync(run.Get<IProcessRunner>(), Cancellation);
        await PublishedAsync(run, design, "design-system");
        _ = Outcomes.Succeeds(await run.Get<IProjectsFolder>().ChooseAsync(projects.Path, Cancellation));
        var workbench = await WelcomedAsync(run);
        var page = run.Page("New job");
        await run.Ui.RunAsync(() => workbench.Page["Welcome"].ExecuteAsync("CloneCommand"));
        var sheet = await run.Ui.ReadAsync(() => page["Chooser"]["Clone"]);
        await run.Ui.RunAsync(() => sheet["Loading"].Value<Task>());
        await ShootBothAsync(run, "66-clone-sheet");

        await run.Ui.RunAsync(() =>
        {
            sheet.Set("Search", "https://github.com/octo/invoices.git");

            return Task.CompletedTask;
        });
        await ShootBothAsync(run, "67-clone-sheet-pasted-address");
    }

    private static async Task PublishedAsync(SimulatedRun run, TemporaryRepository repository, string name) =>
        _ = Outcomes.Succeeds(await run.Get<IRepositoryPublishing>().PublishAsync(repository.Path, new ForgeName("local"), new RepositoryDraft(name, Private: name != "design-system"), Cancellation));

    private static async Task<OpenWorkbench> WelcomedAsync(SimulatedRun run)
    {
        var workbench = await run.WorkbenchAsync();
        await run.Ui.RunAsync(() => workbench.Page["Checking"].Value<Task>());

        return workbench;
    }

    private async Task ShootBothAsync(SimulatedRun run, string name, params Action<Avalonia.Controls.Window>[] arrange)
    {
        await Camera.ShootAsync(run, $"{name}-dark", arrange);
        await Camera.ShootAsync(run, $"{name}-light", ThemeVariant.Light, arrange);
    }

    private static void Gated() =>
        Assert.SkipUnless(FeatureCamera.Folder is not null && HeadlessApp.Renders, $"Set {FeatureCamera.Gate} to a folder and AVALA_HEADLESS_RENDER=1 to capture the feature screens.");
}
