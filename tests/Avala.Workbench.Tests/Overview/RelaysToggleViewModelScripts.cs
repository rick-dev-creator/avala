using Avala.Agents.Contracts.Connections;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Following;
using Avala.Workbench.Overview;
using Avala.Workbench.Spending;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avala.Workbench.Tests.Overview;

public sealed class RelaysToggleViewModelScripts : IDisposable
{
    private static readonly DateTimeOffset One = new(2026, 10, 25, 13, 0, 0, TimeSpan.Zero);

    private readonly FakeHandoffs handoffs = new();
    private readonly JobSummary relayed = Pages.Summary("Migrate the billing service", JobStatus.Running, "claude-max");
    private readonly JobSummary quiet = Pages.Summary("Fix the README", JobStatus.Running, "claude-work");
    private readonly JobBoard board;
    private readonly Pulse pulse;
    private readonly LimitReadings readings;
    private readonly StrongReferenceMessenger messenger = new();

    public RelaysToggleViewModelScripts()
    {
        board = Pages.Board(relayed, quiet);
        pulse = new Pulse(board);
        readings = new LimitReadings(new FakeUsage(), pulse, TimeProvider.System);
        handoffs.Records[relayed.Job] = [new HandoffRecord(relayed.Job, 2, new ConnectionName("claude-work"), new ConnectionName("claude-max"), Avala.Sdk.Option<LimitReason>.None, One)];
    }

    private TestUiDispatcher Ui { get; } = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OnlyAJobThatNearedALimitOffersTheRelaysAsync()
    {
        using var shown = Toggle(relayed.Job);
        using var plain = Toggle(quiet.Job);

        await Ui.PresentedAsync(shown, () => shown.IsShown, () => "the relayed job never offered its relays");

        Assert.False(await Ui.ReadAsync(() => plain.IsShown));
    }

    [Fact]
    public async Task TheToggleAsksForTheShownJobsPaneAndFollowsWhetherItIsOpenAsync()
    {
        var asked = new List<RelaysPaneRequested>();
        messenger.Register<RelaysPaneRequested>(this, (_, message) => asked.Add(message));
        using var toggle = Toggle(relayed.Job);

        await Ui.InvokeAsync(() => ((IRelaysToggleViewModel)toggle).ToggleCommand.Execute(null), Cancellation);
        await Ui.InvokeAsync(() => messenger.Send(new RelaysPaneChanged(relayed.Job, true)), Cancellation);
        await Ui.InvokeAsync(() => messenger.Send(new RelaysPaneChanged(quiet.Job, false)), Cancellation);

        Assert.Equal([new RelaysPaneRequested(relayed.Job)], asked);
        Assert.True(await Ui.ReadAsync(() => toggle.IsOpen));
    }

    public void Dispose()
    {
        readings.Dispose();
        Ui.Dispose();
    }

    private RelaysToggleViewModel Toggle(JobId shown)
    {
        var toggle = new RelaysToggleViewModel(new RelaysReader(board, handoffs, readings), new LiveFeed(pulse, Ui, NullLogger<LiveFeed>.Instance), messenger);
        toggle.OnRegionContextChanged(shown);
        toggle.Activate();

        return toggle;
    }
}
