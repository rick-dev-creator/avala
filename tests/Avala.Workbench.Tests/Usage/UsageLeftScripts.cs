using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Following;
using Avala.Workbench.Navigation;
using Avala.Workbench.Spending;
using Avala.Workbench.Usage;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Workbench.Tests.Usage;

public sealed class UsageLeftViewModelScripts : IDisposable
{
    private static readonly DateTimeOffset FiveHourReset = DateTimeOffset.UnixEpoch.AddHours(4);

    private static readonly DateTimeOffset SevenDayReset = DateTimeOffset.UnixEpoch.AddDays(6);

    private readonly FakeUsage usage = new();
    private readonly TestUiDispatcher ui = new();
    private readonly WeakReferenceMessenger messenger = new();

    [Fact]
    public async Task TheChipShowsWhatIsLeftOfTheMostUsedWindowAndEveryWindowWithItsResetAsync()
    {
        var job = Pages.Summary("Fix JPY rounding in invoice totals", JobStatus.Running, "claude-work");
        Reports("claude-work", 0.72, 0.43);
        var (chip, _) = Chip(Pages.Board(job));

        chip.Activate();
        chip.OnRegionContextChanged(job.Job);

        await ui.PresentedAsync(chip, () => chip.IsShown, () => chip.Left);
        Assert.Equal(("claude-work", "28% left", 0.72, false), await ui.ReadAsync(() => (chip.Connection, chip.Left, chip.Used, chip.IsNear)));
        Assert.Equal(
            [("5-hour window", "72%", $"resets {Amounts(FiveHourReset)}"), ("7-day window", "43%", $"resets {Amounts(SevenDayReset)}")],
            await ui.ReadAsync(() => chip.Windows.Select(window => (window.Label, window.UsedText, window.Resets)).ToList()));
    }

    [Fact]
    public async Task ANewReadingWhileTheJobRunsUpdatesTheChipInPlaceAsync()
    {
        var job = Pages.Summary("Fix JPY rounding in invoice totals", JobStatus.Running, "claude-work");
        var board = Pages.Board(job);
        Reports("claude-work", 0.72, 0.43);
        var (chip, _) = Chip(board);
        chip.Activate();
        chip.OnRegionContextChanged(job.Job);
        await ui.PresentedAsync(chip, () => chip.IsShown, () => chip.Left);
        var window = await ui.ReadAsync(() => chip.Windows[0]);

        Reports("claude-work", 0.8, 0.43);
        board.Publish(board.Jobs);

        await ui.PresentedAsync(chip, () => chip.Left == "20% left", () => chip.Left);
        Assert.Same(window, await ui.ReadAsync(() => chip.Windows[0]));
    }

    [Fact]
    public async Task AWindowCloseToItsLimitMakesTheChipWarnAsync()
    {
        var job = Pages.Summary("Fix JPY rounding in invoice totals", JobStatus.Running, "claude-work");
        Reports("claude-work", 0.4, 0.93);
        var (chip, _) = Chip(Pages.Board(job));

        chip.Activate();
        chip.OnRegionContextChanged(job.Job);

        await ui.PresentedAsync(chip, () => chip.IsShown, () => chip.Left);
        Assert.Equal(("7% left", true), await ui.ReadAsync(() => (chip.Left, chip.IsNear)));
    }

    [Fact]
    public async Task OpeningTheChipShowsTheUsagePageWithItsConnectionMarkedAsync()
    {
        var job = Pages.Summary("Fix JPY rounding in invoice totals", JobStatus.Running, "claude-work");
        Reports("claude-work", 0.72, 0.43);
        usage.Connections.Add(new ConnectionUsage(new ConnectionName("claude-personal"), Pages.Simulator, Pages.Used(1m)));
        var requested = new List<IPage>();
        messenger.Register<List<IPage>, PageRequested>(requested, (list, message) => list.Add(message.Page));
        var (chip, page) = Chip(Pages.Board(job));
        chip.Activate();
        chip.OnRegionContextChanged(job.Job);
        await ui.PresentedAsync(chip, () => chip.IsShown, () => chip.Left);

        await ui.InvokeAsync(() => chip.OpenCommand.Execute(null), TestContext.Current.CancellationToken);
        page.Activate();

        await ui.PresentedAsync(page, () => page.Connections.Count == 2, () => $"{page.Connections.Count} connections");
        Assert.Same(page, Assert.Single(requested));
        Assert.Equal(
            [("claude-work", true), ("claude-personal", false)],
            await ui.ReadAsync(() => page.Connections.Select(meter => (meter.Name, meter.IsFocused)).ToList()));
    }

    [Fact]
    public async Task AJobWhoseConnectionReportsNoWindowShowsNoChipAsync()
    {
        var job = Pages.Summary("Fix JPY rounding in invoice totals", JobStatus.Running, "api-key");
        usage.Connections.Add(new ConnectionUsage(new ConnectionName("api-key"), Pages.Simulator, Pages.Used(1m)));
        var (chip, _) = Chip(Pages.Board(job));
        chip.Activate();

        chip.OnRegionContextChanged(job.Job);

        await ui.PresentedAsync(chip, () => chip.Revision > 0, () => chip.Left);
        Assert.Equal((false, false), await ui.ReadAsync(() => (chip.IsShown, chip.OpenCommand.CanExecute(null))));
    }

    public void Dispose() => ui.Dispose();

    private static string Amounts(DateTimeOffset at) => Presenting.Amounts.Time(at);

    private void Reports(string connection, double fiveHour, double sevenDay)
    {
        var reported = new ConnectionUsage(
            new ConnectionName(connection),
            Pages.Simulator,
            Pages.Used(1m, new UsageLimit("5h", fiveHour, FiveHourReset), new UsageLimit("7d", sevenDay, SevenDayReset)));
        var index = usage.Connections.FindIndex(known => known.Connection == reported.Connection);

        if (index < 0)
        {
            usage.Connections.Add(reported);
        }
        else
        {
            usage.Connections[index] = reported;
        }
    }

    private (UsageLeftViewModel Chip, UsageViewModel Page) Chip(JobBoard board)
    {
        var readings = Pages.Readings(usage);
        var spending = new JobSpending(usage, new FakeBudgets(), new FakeSupervision(), new SessionBook(new FakeUsage()));
        var page = new UsageViewModel(new UsageReader(readings, spending, board), new UsageWindows(new FakeUsageHistory(), new FakeTimeProvider()), new LiveFeed(new Pulse(board), ui));
        var chip = new UsageLeftViewModel(new JobAllowance(readings, spending, board), new LiveFeed(new Pulse(board), ui), new JobFocus(new TestRegions(), messenger), page);

        return (chip, page);
    }
}
