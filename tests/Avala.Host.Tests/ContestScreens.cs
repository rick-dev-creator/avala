using System.Windows.Input;
using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Testing.UI;
using Avalonia.Styling;

namespace Avala.Host.Tests;

public sealed class ContestScreens(HeadlessUi ui, PublishedPlugins plugins)
{
    private FeatureCamera Camera { get; } = new(ui, FeatureCamera.Folder ?? string.Empty);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheCandidatesOfTheNewJobPageTheContestRowItsInspectorSectionAndTheReviewsSwitcherAsync()
    {
        Assert.SkipUnless(FeatureCamera.Folder is not null && HeadlessApp.Renders, $"Set {FeatureCamera.Gate} to a folder and AVALA_HEADLESS_RENDER=1 to capture the feature screens.");
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            (".avala/checks.json", ReviewTests.CalculatorChecks),
            (".avala/permissions.json", ReviewTests.TestsPolicy),
            (".avala/jobs.json", """{ "bestOf": { "candidates": 3, "max": 4 } }"""),
            (".avala/budget.json", """{ "costPerJob": { "USD": 0.60 } }"""));
        var decided = run.Watch<ContestDecided>();
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
            page.Set("Instruction", "[simulate: contest] Fix the calculator so that add(2, 2) is 4");
        });

        await Camera.ShootAsync(run, "contests-01-new-job-candidates-dark");
        await Camera.ShootAsync(run, "contests-01-new-job-candidates-light", ThemeVariant.Light);

        await run.Ui.RunAsync(() => page.ExecuteAsync("SubmitCommand"));
        var winner = Outcomes.Present(Outcomes.Present((await decided.UntilAsync(_ => true)).Contest.Verdict).Winner);
        _ = await workbench.SelectAsync(winner);
        await run.Ui.InvokeAsync(() => Assert.Single(workbench.Sidebar["ReadyForReview"].Items).Execute("ToggleCommand"), Cancellation);

        await Camera.ShootAsync(run, "contests-02-board-contest-row-dark");
        await Camera.ShootAsync(run, "contests-02-board-contest-row-light", ThemeVariant.Light);

        _ = await workbench.InspectAsync(winner, "ContestSectionViewModel");
        await Camera.ShootAsync(run, "contests-03-inspector-contest-section-dark", FeatureCamera.Unfold("Contest"));
        await Camera.ShootAsync(run, "contests-03-inspector-contest-section-light", ThemeVariant.Light, FeatureCamera.Unfold("Contest"));

        await workbench.ShowsAsync(() => ((ICommand)workbench.Page["OpenReviewCommand"].Target).CanExecute(null));
        await run.Ui.RunAsync(() => workbench.Page.ExecuteAsync("OpenReviewCommand"));
        await Camera.ShootAsync(run, "contests-04-review-candidate-switcher-dark");
        await Camera.ShootAsync(run, "contests-04-review-candidate-switcher-light", ThemeVariant.Light);
    }
}
