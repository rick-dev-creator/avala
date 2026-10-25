using System.Globalization;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Following;
using Avala.Workbench.Overview;
using Avala.Workbench.Spending;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Workbench.Tests.Overview;

public sealed class RelaysPaneViewModelScripts : IDisposable
{
    private static readonly DateTimeOffset One = new(2026, 10, 25, 13, 0, 0, TimeSpan.Zero);

    private static readonly ConnectionName Work = new("claude-work");

    private static readonly ConnectionName Max = new("claude-max");

    private readonly FakeTimeProvider clock = new(One.AddHours(3));
    private readonly FakeHandoffs handoffs = new();
    private readonly FakeUsage usage = new();
    private readonly JobBoard board;
    private readonly Pulse pulse;
    private readonly LimitReadings readings;
    private readonly JobSummary job = Pages.Summary("Migrate the billing service", JobStatus.Running, "claude-max") with { Submitted = One };
    private readonly JobSummary quiet = Pages.Summary("Fix the README", JobStatus.Running, "claude-work");

    public RelaysPaneViewModelScripts()
    {
        board = Pages.Board(job, quiet);
        pulse = new Pulse(board);
        readings = new LimitReadings(usage, pulse, clock);
        handoffs.Records[job.Job] = [new HandoffRecord(job.Job, 2, Work, Max, new LimitReason(Work, "5h", 0.91, 0.9), One.AddHours(2))];
        handoffs.Marks[job.Job] = [new RelayMark(job.Job, RelayMarkKind.WindingDown, Work, One.AddHours(2).AddMinutes(-3)) { Why = new LimitReason(Work, "5h", 0.91, 0.9) }];
        usage.Connections.Add(new ConnectionUsage(Work, Pages.Simulator, Pages.Used(1m, new UsageLimit("5h", 0.91, One.AddHours(4)))));
        usage.Connections.Add(new ConnectionUsage(Max, Pages.Simulator, Pages.Used(1m, new UsageLimit("5h", 0.38, One.AddHours(6)))));
        Messenger.Register<RelaysPaneChanged>(this, (_, message) => Changes.Add(message));
    }

    private TestUiDispatcher Ui { get; } = new();

    private StrongReferenceMessenger Messenger { get; } = new();

    private List<RelaysPaneChanged> Changes { get; } = [];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheOpenedPaneShowsTheJourneyAcrossConnectionsAndWhenEachResetsAsync()
    {
        using var pane = Pane(job.Job);

        await Ui.InvokeAsync(() => Messenger.Send(new RelaysPaneRequested(job.Job)), Cancellation);
        await Ui.PresentedAsync(pane, () => pane.HasJourney, () => "the journey never showed");

        var shown = await Ui.ReadAsync(() => (
            pane.Caption,
            pane.Headline,
            pane.Subline,
            pane.Lanes.Select(lane => (lane.Name, lane.Meter.Reading, lane.IsCurrent)).ToList(),
            pane.Legs.Select(leg => (leg.Title, leg.Detail, leg.IsCurrent)).ToList(),
            pane.Course.Lanes,
            pane.Footer));
        Assert.Equal(("Running on claude-max", $"Took over from claude-work at 91% · {Clock(One.AddHours(2))}"), (shown.Headline, shown.Subline));
        Assert.Equal("2 connections · 1 handoff", shown.Caption);
        Assert.Equal([("claude-work", $"91% · resets {Clock(One.AddHours(4))}", false), ("claude-max", $"38% · resets {Clock(One.AddHours(6))}", true)], shown.Item4);
        Assert.Equal([("claude-work", "Ran 2h 0m · wound down at 91%", false), ("claude-max", "Running · 38% of its window", true)], shown.Item5);
        Assert.Equal(2, shown.Lanes);
        Assert.Equal($"claude-work resets first, at {Clock(One.AddHours(4))}", shown.Footer);
        Assert.Equal([new RelaysPaneChanged(job.Job, true)], Changes);
    }

    [Fact]
    public async Task ANewRelayMarkReachesTheOpenPaneAsync()
    {
        using var pane = Pane(job.Job);
        await Ui.InvokeAsync(() => Messenger.Send(new RelaysPaneRequested(job.Job)), Cancellation);
        await Ui.PresentedAsync(pane, () => pane.HasJourney, () => "the journey never showed");

        var capped = new RelayMark(job.Job, RelayMarkKind.Capped, Max, One.AddHours(3)) { Why = new LimitReason(Max, "5h", 0.97, 0.9) };
        handoffs.Marks[job.Job] = [.. handoffs.Marks[job.Job], capped];
        await pulse.HandleAsync(new RelayMarked(capped), Cancellation);

        await Ui.PresentedAsync(pane, () => pane.Course.Stops.Any(stop => stop.Kind == Components.Graphs.RelayStopKind.Capped), () => "the cap never reached the pane");

        Assert.Equal("Running · 38% of its window", await Ui.ReadAsync(() => pane.Legs[^1].Detail));
    }

    [Fact]
    public async Task ClosingThePaneTellsTheToggleAsync()
    {
        using var pane = Pane(job.Job);
        await Ui.InvokeAsync(() => Messenger.Send(new RelaysPaneRequested(job.Job)), Cancellation);

        await Ui.InvokeAsync(() => ((IRelaysPaneViewModel)pane).CloseCommand.Execute(null), Cancellation);

        Assert.False(await Ui.ReadAsync(() => pane.IsOpen));
        Assert.Equal([new RelaysPaneChanged(job.Job, true), new RelaysPaneChanged(job.Job, false)], Changes);
    }

    public void Dispose()
    {
        readings.Dispose();
        Ui.Dispose();
    }

    private static string Clock(DateTimeOffset at) => at.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    private RelaysPaneViewModel Pane(JobId shown)
    {
        var pane = new RelaysPaneViewModel(new RelaysReader(board, handoffs, readings), Feed(), Messenger);
        pane.OnRegionContextChanged(shown);
        pane.Activate();

        return pane;
    }

    private LiveFeed Feed() => new(pulse, Ui, NullLogger<LiveFeed>.Instance, clock);
}
