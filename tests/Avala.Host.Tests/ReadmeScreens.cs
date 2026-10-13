using System.Windows.Input;
using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Preview.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using SvgDrawing = Avalonia.Svg.Skia.Svg;

namespace Avala.Host.Tests;

public sealed class ReadmeScreens(HeadlessUi ui, PublishedPlugins plugins)
{
    private const string PassingChecks = """{ "checks": [ { "name": "git", "command": "git", "arguments": ["--version"], "timeoutSeconds": 60 } ] }""";

    private const string ServicesPolicy = """{ "rules": [ { "name": "services", "kind": "command", "target": "dotnet *", "answer": "allow" } ] }""";

    private const string Package = """{ "name": "orders", "scripts": { "dev": "vite --port $AVALA_PORT", "build": "vite build" } }""";

    private const string Request = "Chart the jobs we finished this week";

    private static readonly TimeSpan Pace = TimeSpan.FromMilliseconds(97);

    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / FeatureReel.FramesPerSecond);

    private FeatureReel Reel { get; } = new(ui, FeatureCamera.Folder ?? string.Empty);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheHeroFromARequestOnTheWelcomeToAChartDrawnInTheConversationAsync()
    {
        Gated();
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], [], pace: Pace);
        var workbench = await run.WorkbenchAsync();
        await run.Ui.RunAsync(() => workbench.Page["Checking"].Value<Task>());
        var welcome = workbench.Page["Welcome"];

        await Reel.FilmAsync(run, "hero", [FeatureReel.Hold(1.2), .. Typed(welcome, Request), FeatureReel.Hold(0.8)]);

        var progress = run.Watch<JobProgressed>();
        await run.Ui.RunAsync(() =>
        {
            welcome.Set("Instruction", $"{Request}\n[simulate: animated-canvas]");

            return welcome.ExecuteAsync("StartCommand");
        });
        var chart = new ChartClock();
        await StreamAsync(run, progress.UntilAsync(update => update.Status == JobStatus.AwaitingReview), () => FilmChartAsync(run, chart));
        var settled = chart.Elapsed;

        await Reel.FilmAsync(
            run,
            "hero",
            [.. Enumerable.Range(1, FeatureReel.Frames(3.0)).Select(frame => new ReelFrame(window => ChartClock.Show(window, settled + (Frame * frame))))]);
    }

    [Fact]
    public async Task ThePermissionCardAllowingTheProgramForTheRestOfTheJobAsync()
    {
        Gated();
        await using var run = await SimulatedRun.InstructedAsync(plugins, "Apply the new orders migration\n[simulate: permission]", [], [(".avala/checks.json", PassingChecks)]);
        _ = await run.DecisionAsync();
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() => Card(conversation) is { } card && card["AwaitsYou"].Value<bool>());

        await Reel.FilmAsync(
            run,
            "permission",
            FeatureReel.Hold(1.2),
            new ReelFrame(window => Point(window, "Always dotnet in this job"), FeatureReel.Frames(0.6)),
            new ReelFrame(_ => Card(conversation)!.Value["Scope"].Choose("ChooseCommand", "Always"), FeatureReel.Frames(1.0)),
            new ReelFrame(window => Point(window, "Allow"), FeatureReel.Frames(0.6)));

        await run.Ui.RunAsync(() => Card(conversation)!.Value.ExecuteAsync("AllowCommand"));
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await workbench.ShowsInGroupAsync(run.Job, "ReadyForReview");
        await Reel.FilmAsync(run, "permission", FeatureReel.Hold(2.5));
    }

    [Fact]
    public async Task TheReviewSheetFromItsVerdictToTheDiffAndTheApprovalAsync()
    {
        Gated();
        await using var run = await SimulatedRun.InstructedAsync(
            plugins,
            "Fix the calculator so that add(2, 2) is 4\n[simulate: fix-after-feedback]",
            [],
            [(".avala/checks.json", ReviewTests.CalculatorChecks), (".avala/permissions.json", ReviewTests.TestsPolicy), ("calculator.txt", "add(2, 2) = 5\n")]);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() => ((ICommand)workbench.Page["OpenReviewCommand"].Target).CanExecute(null));
        await Reel.FilmAsync(run, "review", new ReelFrame(window => Point(window, "Review"), FeatureReel.Frames(1.0)));

        await run.Ui.RunAsync(() => workbench.Page.ExecuteAsync("OpenReviewCommand"));
        var review = await run.Ui.ReadAsync(() => workbench.Page["Review"]);
        await run.Ui.RunAsync(() => review["Files"].Items[0].ExecuteAsync("ShowHunksCommand"));
        await Reel.FilmAsync(
            run,
            "review",
            FeatureReel.Hold(1.6),
            new ReelFrame(Unfolded, FeatureReel.Frames(2.0)),
            new ReelFrame(window => { Unfolded(window); Point(window, "Approve"); }, FeatureReel.Frames(0.6)));

        await run.Ui.RunAsync(() => review.ExecuteAsync("ApproveCommand"));
        await workbench.ShowsAsync(() => review["Outcome"].Text.Length > 0);
        await Reel.FilmAsync(run, "review", new ReelFrame(Unfolded, FeatureReel.Frames(2.5)));
    }

    [Fact]
    public async Task ThePreviewPaneOpeningByItselfWhenTheJobStartsAServerAsync()
    {
        Gated();
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            [("resources.json", $$"""{ {{LeasedPorts.Settings(7)}}, "sampleSeconds": 0.2 }""")],
            [(".avala/permissions.json", ServicesPolicy), ("package.json", Package)],
            pace: Pace);
        var available = run.Watch<PreviewAvailable>();
        var progress = run.Watch<JobProgressed>();
        var workbench = await run.WorkbenchAsync();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "Start the orders service and show it\n[simulate: processes]")));
        _ = await workbench.SelectAsync(job);
        await StreamAsync(run, progress.UntilAsync(update => update.Job == job && update.Status == JobStatus.AwaitingReview), () => Reel.FilmAsync(run, "preview", FeatureReel.Hold(1.0 / 6)));
        await Reel.FilmAsync(run, "preview", FeatureReel.Hold(0.6));

        run.Clock.Advance(TimeSpan.FromSeconds(0.2));
        _ = await available.UntilAsync(_ => true);
        var pane = Assert.Single(await run.Ui.ReadAsync(() => workbench.Page["Regions"]["Preview"].Items));
        await workbench.ShowsAsync(() => pane["IsOpen"].Value<bool>());
        await Reel.FilmAsync(run, "preview", FeatureReel.Hold(3.0));
    }

    [Fact]
    public async Task TheWelcomeWithItsHarnessMenuAndAReplyWithHighlightedCodeAsync()
    {
        Gated();
        await using var run = await SimulatedRun.InstructedAsync(plugins, "Fix the rounding of JPY amounts\n[simulate: markdown]", [], []);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var workbench = await run.WorkbenchAsync();
        await run.Ui.RunAsync(() => workbench.Page["Checking"].Value<Task>());
        var welcome = workbench.Page["Welcome"];
        await run.Ui.RunAsync(() =>
        {
            welcome.Set("Instruction", "Add a dark mode toggle to the settings page");
            welcome["Harness"].Set("IsOpen", true);

            return Task.CompletedTask;
        });
        await Reel.FilmAsync(run, "welcome", FeatureReel.Hold(0));

        await run.Ui.RunAsync(() =>
        {
            welcome["Harness"].Set("IsOpen", false);

            return Task.CompletedTask;
        });
        _ = await workbench.SelectAsync(run.Job);
        await Reel.FilmAsync(run, "conversation", FeatureReel.Hold(0));
    }

    [Fact]
    public async Task TheReviewOfTheBestOfThreeCandidatesAsync()
    {
        Gated();
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            (".avala/checks.json", ReviewTests.CalculatorChecks),
            (".avala/permissions.json", ReviewTests.TestsPolicy),
            (".avala/jobs.json", """{ "bestOf": { "candidates": 3, "max": 3 } }"""),
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
            page.Set("Instruction", "Fix the calculator so that add(2, 2) is 4\n[simulate: contest]");
            await page.ExecuteAsync("SubmitCommand");
        });

        var winner = Outcomes.Present(Outcomes.Present((await decided.UntilAsync(_ => true)).Contest.Verdict).Winner);
        _ = await workbench.SelectAsync(winner);
        await run.Ui.InvokeAsync(() => Assert.Single(workbench.Sidebar["ReadyForReview"].Items).Execute("ToggleCommand"), Cancellation);
        _ = await workbench.InspectAsync(winner, "ContestSectionViewModel");
        await workbench.ShowsAsync(() => ((ICommand)workbench.Page["OpenReviewCommand"].Target).CanExecute(null));
        await run.Ui.RunAsync(() => workbench.Page.ExecuteAsync("OpenReviewCommand"));
        await Reel.FilmAsync(run, "contest", new ReelFrame(FeatureCamera.Unfold("Contest"), 1));
    }

    private static async Task StreamAsync(SimulatedRun run, Task settled, Func<Task> film)
    {
        var paced = run.PacedAsync();

        while (await Task.WhenAny(paced, settled) == paced)
        {
            await run.DeliveredAsync();
            await film();
            paced = run.PacedAsync();
            run.Pace();
        }

        await run.DeliveredAsync();
        await film();
    }

    private async Task FilmChartAsync(SimulatedRun run, ChartClock chart)
    {
        await Reel.FilmAsync(run, "hero", new ReelFrame(chart.Show));
        chart.Advance();
    }

    private static IEnumerable<ReelFrame> Typed(Bound welcome, string text) =>
        Enumerable.Range(1, (text.Length + 1) / 2).Select(typed => new ReelFrame(_ => welcome.Set("Instruction", text[..Math.Min(text.Length, typed * 2)])));

    private static void Point(Window window, string text)
    {
        var target = window.GetVisualDescendants().OfType<TextBlock>().Last(block => block.IsEffectivelyVisible && block.Text == text);
        window.MouseMove(target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value);
    }

    private static void Unfolded(Window window)
    {
        foreach (var fold in window.GetVisualDescendants().OfType<Avala.Components.UI.Fold>().Where(fold => fold.Name == "Changes"))
        {
            fold.IsExpanded = true;
        }
    }

    private static Bound? Card(Bound conversation) =>
        conversation["Entries"].Items.Cast<Bound?>().FirstOrDefault(entry => entry!.Value.Kind == "PermissionCardViewModel");

    private static void Gated() =>
        Assert.SkipUnless(FeatureCamera.Folder is not null && HeadlessApp.Renders, $"Set {FeatureCamera.Gate} to a folder and AVALA_HEADLESS_RENDER=1 to capture the feature screens.");

    private sealed class ChartClock
    {
        private bool drawn;

        public TimeSpan Elapsed { get; private set; }

        public static bool Show(Window window, TimeSpan elapsed)
        {
            var drawing = window.GetVisualDescendants().OfType<SvgDrawing>().FirstOrDefault()?.SkSvg;
            drawing?.SetAnimationTime(elapsed);

            return drawing is not null;
        }

        public void Show(Window window) => drawn = Show(window, Elapsed);

        public void Advance()
        {
            if (drawn)
            {
                Elapsed += Frame;
            }
        }
    }
}
