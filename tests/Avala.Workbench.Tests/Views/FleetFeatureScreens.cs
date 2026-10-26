using Avala.Fleet.Contracts;
using Avala.Testing;
using Avala.Testing.UI;
using Avala.Workbench.FleetActivity;
using Avala.Workbench.Tests.FleetActivity;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Workbench.Tests.Views;

public sealed class FleetFeatureScreens(HeadlessUi ui)
{
    private const string Gate = "AVALA_FEATURE_SCREENS";

    private static string? Folder => Environment.GetEnvironmentVariable(Gate) is { Length: > 0 } found ? found : null;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string, int, int, bool> Shots => new()
    {
        { "dark", 1240, 1100, false },
        { "dark", 1240, 1100, true },
        { "light", 1240, 1100, false },
        { "dark", 720, 1900, false },
        { "dark", 380, 3000, true },
    };

    [Theory]
    [MemberData(nameof(Shots))]
    public Task TheFleetDashboardAsync(string variant, int width, int height, bool tokens)
    {
        Assert.SkipUnless(Folder is not null && HeadlessApp.Renders, $"Set {Gate} to a folder and AVALA_HEADLESS_RENDER=1 to capture the fleet screens.");

        return ui.RunAsync(async () =>
        {
            var activity = new FakeFleetActivity { Answer = query => new FleetUsageView(FleetReadState.Read, query.To == new DateOnly(2026, 10, 19) ? Fleet(0.8m) : Fleet(1m)) };
            var page = new FleetActivityViewModel(activity, new InlineUiDispatcher(), new FakeTimeProvider(FakeFleetActivity.Noon));
            page.Activate();
            await page.Loading;

            if (tokens)
            {
                page.Trend.ShowTokensCommand.Execute(null);
            }

            var view = Screen.Show(page, variant == "light" ? ThemeVariant.Light : ThemeVariant.Dark, width, height);
            Components.UI.Theme.Motion.SetIsReduced(view.Window, true);
            Pump(view);

            var path = Path.Combine(Directory.CreateDirectory(Folder).FullName, $"fleet-{variant}-{width}{(tokens ? "-tokens" : string.Empty)}.png");
            view.Window.CaptureRenderedFrame()?.Save(path, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

            Assert.True(File.Exists(path));
        }, Cancellation);
    }

    private static FleetUsage Fleet(decimal scale)
    {
        FleetCost[] Usd(decimal amount) => [new FleetCost("USD", "apiEquivalent", Math.Round(amount * scale, 2))];
        FleetTokens Tokens(decimal millions) => new(millions * 90_000 * scale, millions * 30_000 * scale, millions * 820_000 * scale, millions * 45_000 * scale, millions * 15_000 * scale);
        decimal[] days = [3.2m, 5.1m, 4.4m, 7.8m, 6.3m, 1.9m, 2.6m];
        FleetShare Share(FleetShareKind kind, string key, decimal amount, int jobs) => new(kind, key, key, Tokens(amount), Usd(amount * 3.4m), jobs);

        return new FleetUsage(
            FakeFleetActivity.Usd,
            new FleetTotals(Tokens(days.Sum()), Usd(days.Sum() * 3.4m), (int)(41 * scale), 3),
            [.. days.Select((amount, index) => new FleetPeriod(new DateOnly(2026, 10, 19).AddDays(index), Tokens(amount), Usd(amount * 3.4m)))],
            [
                Share(FleetShareKind.Repository, "remote:github.com/rick/avala", 18.2m * scale, 24),
                Share(FleetShareKind.Repository, "remote:github.com/rick/kasumi", 9.4m, 12),
                Share(FleetShareKind.Repository, "remote:github.com/rick/homelab", 2.1m, 4),
                Share(FleetShareKind.OutsideAnyJob, string.Empty, 1.6m, 0),
            ],
            [
                Share(FleetShareKind.Instance, "worker-2", 17.5m * scale, 22),
                Share(FleetShareKind.Instance, "laptop", 10.1m, 15),
                Share(FleetShareKind.Instance, "desktop", 3.7m, 4),
            ],
            [
                Share(FleetShareKind.Model, "claude-opus-5-5", 25.9m * scale, 33),
                Share(FleetShareKind.Model, "claude-sonnet-5-5", 4.1m, 7),
                Share(FleetShareKind.Model, "claude-haiku-5-5", 1.3m, 1),
            ]);
    }

    private static void Pump(ViewScript view)
    {
        for (var frame = 0; frame < 60; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        view.Settle();
    }
}
