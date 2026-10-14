using System.Collections.Immutable;
using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;

namespace Avala.Workbench.Spending;

internal sealed class WindowWatch(JobBoard board)
{
    public const double Moved = 0.02;

    private const double Unchanged = 1e-9;

    private ImmutableDictionary<(ConnectionName Connection, string Window), Seen> seen = ImmutableDictionary<(ConnectionName, string), Seen>.Empty;

    public IReadOnlyList<LimitReading> Watched(ConnectionName connection, IReadOnlyList<LimitReading> readings)
    {
        var idle = !board.Jobs.Values.Any(job => job.Summary.Status == JobStatus.Running && job.Summary.Connection == Option<ConnectionName>.Some(connection));

        return [.. readings.Select(reading => reading with { Outside = Watch((connection, reading.Limit.Window), reading.Used, idle) })];
    }

    private double Watch((ConnectionName, string) key, double used, bool idle) =>
        ImmutableInterlocked.AddOrUpdate(ref seen, key, _ => new Seen(used, idle, 0), (_, last) => Next(last, used, idle)).Outside;

    private static Seen Next(Seen last, double used, bool idle)
    {
        if (Math.Abs(used - last.Used) < Unchanged)
        {
            return last with { IdleSince = last.IdleSince || idle };
        }

        var outside = used < last.Used ? 0
            : used - last.Used >= Moved && last.IdleSince ? last.Outside + (used - last.Used)
            : last.Outside;

        return new Seen(used, idle, outside);
    }

    private sealed record Seen(double Used, bool IdleSince, double Outside);
}
