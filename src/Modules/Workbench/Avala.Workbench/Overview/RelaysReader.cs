using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Spending;

namespace Avala.Workbench.Overview;

internal sealed record RelayGauge(ConnectionName Connection, string Harness, Option<UsageLimit> Tightest);

internal sealed record RelaysView(RelayJourney Journey, IReadOnlyList<RelayGauge> Gauges, JobStatus Status, DateTimeOffset Now);

internal sealed class RelaysReader(JobBoard board, IHandoffs handoffs, LimitReadings readings)
{
    public bool Offers(JobId job) =>
        handoffs.OfJob(job).Count > 0 || handoffs.MarksOf(job).Count > 0 || handoffs.WaitOf(job).IsSome;

    public Option<RelaysView> Read(JobId job) =>
        board.Find(job).Map(found => View(found.Summary));

    private RelaysView View(JobSummary summary)
    {
        var now = readings.Now;
        var journey = RelayJourneys.Of(summary, handoffs.OfJob(summary.Job), handoffs.MarksOf(summary.Job), handoffs.WaitOf(summary.Job), now);
        var usage = readings.ByConnection();

        return new RelaysView(
            journey,
            [.. journey.Connections.Select(connection => Gauge(connection, usage.FirstOrDefault(found => found.Connection == connection)))],
            summary.Status,
            now);
    }

    private RelayGauge Gauge(ConnectionName connection, Observability.Contracts.ConnectionUsage? usage) =>
        usage is null
            ? new RelayGauge(connection, string.Empty, Option<UsageLimit>.None)
            : new RelayGauge(
                connection,
                usage.Provider.Name,
                readings.Judged(usage.Usage.Limits).OrderByDescending(reading => reading.Used).Select(reading => Option<UsageLimit>.Some(reading.Expired ? reading.Limit with { UsedFraction = 0 } : reading.Limit)).FirstOrDefault(Option<UsageLimit>.None));
}
