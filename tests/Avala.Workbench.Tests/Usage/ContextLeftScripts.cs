using System.Collections.Immutable;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Following;
using Avala.Workbench.Timeline;
using Avala.Workbench.Usage;

namespace Avala.Workbench.Tests.Usage;

public sealed class ContextLeftViewModelScripts : IDisposable
{
    private static readonly SessionId Session = SessionId.New();

    private readonly TestUiDispatcher ui = new();
    private readonly JobBoard board = new();
    private readonly JobSummary job = Pages.Summary("Rename the orders module", JobStatus.Running, "claude-work");

    [Fact]
    public async Task TheGaugeShowsTheShareOfTheWindowInUseWithItsTokensAndModelAsync()
    {
        Shows(new ModelReported(Session, Turn, "claude-opus-5-5"), new ContextReported(Session, Turn, 141_000, 200_000));
        using var gauge = Gauge();

        await ui.PresentedAsync(gauge, () => gauge.IsShown, () => gauge.Share);

        Assert.Equal(
            ("30% context left", 0.705, false, "141,000 of 200,000 tokens in use", "claude-opus-5-5", false),
            await ui.ReadAsync(() => (gauge.Share, gauge.Used, gauge.IsNear, gauge.Tokens, gauge.Model, gauge.WasCompacted)));
    }

    [Fact]
    public async Task EachTurnsReportUpdatesTheGaugeAsync()
    {
        Shows(new ContextReported(Session, Turn, 40_000, 200_000));
        using var gauge = Gauge();
        await ui.PresentedAsync(gauge, () => gauge.IsShown, () => gauge.Share);

        Shows(new ContextReported(Session, Turn, 40_000, 200_000), new ContextReported(Session, Turn, 90_000, 200_000));

        await ui.PresentedAsync(gauge, () => gauge.Share == "55% context left", () => gauge.Share);
        Assert.Equal("90,000 of 200,000 tokens in use", await ui.ReadAsync(() => gauge.Tokens));
    }

    [Fact]
    public async Task ACompactionShowsTheSmallerContextAndSaysItWasCompactedAsync()
    {
        Shows(new ContextReported(Session, Turn, 31_000, 200_000) { Compacted = true });
        using var gauge = Gauge();

        await ui.PresentedAsync(gauge, () => gauge.IsShown, () => gauge.Share);

        Assert.Equal(
            ("85% context left", true, "Compacted during the last turn, so older messages were summarized."),
            await ui.ReadAsync(() => (gauge.Share, gauge.WasCompacted, gauge.Compaction)));
    }

    [Fact]
    public async Task AContextCloseToFullMakesTheGaugeWarnAsync()
    {
        Shows(new ContextReported(Session, Turn, 185_000, 200_000));
        using var gauge = Gauge();

        await ui.PresentedAsync(gauge, () => gauge.IsShown, () => gauge.Share);

        Assert.Equal(("8% context left", true), await ui.ReadAsync(() => (gauge.Share, gauge.IsNear)));
    }

    [Fact]
    public async Task AJobWhoseHarnessReportedNoContextShowsNoGaugeAsync()
    {
        Shows(new ModelReported(Session, Turn, "claude-opus-5-5"));
        using var gauge = Gauge();

        await ui.PresentedAsync(gauge, () => gauge.Revision > 0, () => gauge.Share);

        Assert.False(await ui.ReadAsync(() => gauge.IsShown));
    }

    public void Dispose() => ui.Dispose();

    private static TurnId Turn { get; } = TurnId.New();

    private void Shows(params IAgentEvent[] reported)
    {
        var transcript = reported.Aggregate(Transcript.Empty, (current, next) => current.Apply(next, DateTimeOffset.UnixEpoch));
        board.Publish(ImmutableDictionary<JobId, BoardJob>.Empty.Add(job.Job, new BoardJob(job, transcript)));
    }

    private ContextLeftViewModel Gauge()
    {
        var gauge = new ContextLeftViewModel(board, new LiveFeed(new Pulse(board), ui));
        gauge.Activate();
        gauge.OnRegionContextChanged(job.Job);

        return gauge;
    }
}
