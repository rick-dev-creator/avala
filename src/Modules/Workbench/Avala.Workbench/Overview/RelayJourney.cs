using Avala.Agents.Contracts.Connections;
using Avala.Components.Graphs;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Overview;

internal enum RelayLegKind
{
    Ran,
    Running,
    Waiting,
}

internal sealed record RelayLeg(ConnectionName Connection, RelayLegKind Kind, DateTimeOffset From, DateTimeOffset To)
{
    public Option<HandoffRecord> Handoff { get; init; }

    public Option<RelayMark> Ending { get; init; }

    public bool Compacting { get; init; }

    public Option<DateTimeOffset> ResumesAt { get; init; }

    public TimeSpan Length => To - From;
}

internal sealed record RelayJourney(IReadOnlyList<ConnectionName> Connections, IReadOnlyList<RelayLeg> Legs, IReadOnlyList<RelayMark> Stops)
{
    public static RelayJourney Empty { get; } = new([], [], []);

    public int Handoffs => Legs.Count(leg => leg.Handoff.IsSome);

    public bool IsEmpty => Legs.Count == 0;

    public Option<RelayLeg> Current => Legs is [.., var last] ? last : Option<RelayLeg>.None;

    public RelayCourse Course()
    {
        if (IsEmpty)
        {
            return RelayCourse.Empty;
        }

        var stretches = Legs.Select(leg => new RelayStretch(Lane(leg.Connection), Position(leg.From), Position(leg.To), Stretch(leg.Kind))).ToList();
        var changes = Legs.Zip(Legs.Skip(1)).Where(pair => pair.First.Connection != pair.Second.Connection)
            .Select(pair => new RelayStop(Lane(pair.Second.Connection), Position(pair.Second.From), pair.Second.Handoff.IsSome || pair.First.Handoff.IsSome ? RelayStopKind.HandedOff : RelayStopKind.Resumed));
        var stops = Stops.Select(mark => new RelayStop(Lane(mark.Connection), Position(mark.At), Stop(mark.Kind)));

        return new RelayCourse(Connections.Count, stretches, [.. stops.Concat(changes)]);
    }

    public double Position(DateTimeOffset at)
    {
        if (IsEmpty)
        {
            return 0;
        }

        var start = Legs[0].From;
        var span = (Legs[^1].To - start).Ticks;
        var index = Legs.TakeWhile(leg => leg.To < at).Count();

        if (index >= Legs.Count)
        {
            return 1;
        }

        var leg = Legs[index];
        var within = leg.Length.Ticks <= 0 ? 0 : Math.Clamp((at - leg.From).Ticks / (double)leg.Length.Ticks, 0, 1);
        var even = (index + within) / Legs.Count;
        var timed = span <= 0 ? even : Math.Clamp((at - start).Ticks / (double)span, 0, 1);

        return (even + timed) / 2;
    }

    private int Lane(ConnectionName connection) => Math.Max(0, Connections.ToList().IndexOf(connection));

    private static RelayStretchKind Stretch(RelayLegKind kind) => kind switch
    {
        RelayLegKind.Running => RelayStretchKind.Running,
        RelayLegKind.Waiting => RelayStretchKind.Waiting,
        _ => RelayStretchKind.Ran,
    };

    private static RelayStopKind Stop(RelayMarkKind kind) => kind switch
    {
        RelayMarkKind.Capped => RelayStopKind.Capped,
        RelayMarkKind.Compacting => RelayStopKind.Compacting,
        _ => RelayStopKind.WindingDown,
    };
}

internal static class RelayJourneys
{
    private static readonly TimeSpan Slack = TimeSpan.FromSeconds(1);

    public static RelayJourney Of(JobSummary summary, IReadOnlyList<HandoffRecord> handoffs, IReadOnlyList<RelayMark> marks, Option<ResetWait> wait, DateTimeOffset now)
    {
        var switches = Switches(handoffs, marks, wait);

        if (switches.Count == 0 && !marks.Any(mark => mark.Kind is RelayMarkKind.WindingDown or RelayMarkKind.Capped))
        {
            return RelayJourney.Empty;
        }

        var first = handoffs.Select(handoff => handoff.From).Concat(marks.Select(mark => mark.Connection))
            .Select(Option<ConnectionName>.Some).FirstOrDefault(summary.Connection);
        var legs = new List<RelayLeg>();
        var current = new RelayLeg(first.Match(name => name, () => new ConnectionName("default connection")), RelayLegKind.Ran, summary.Submitted, summary.Submitted);

        foreach (var change in switches)
        {
            legs.Add(Closed(current, change.At, marks) with { Handoff = change.Handoff });
            current = new RelayLeg(change.Connection, change.Kind, change.At, change.At) { ResumesAt = change.ResumesAt };
        }

        legs.Add(Last(Closed(current, summary.Ended.Match(ended => ended, () => now), marks), summary.Status));
        IReadOnlyList<RelayLeg> kept = [.. legs.Where((leg, index) => index == legs.Count - 1 || leg.To > leg.From || leg.Handoff.IsSome)];

        return new RelayJourney(
            [.. kept.Select(leg => leg.Connection).Distinct()],
            kept,
            [.. marks.Where(mark => mark.Kind is RelayMarkKind.WindingDown or RelayMarkKind.Capped or RelayMarkKind.Compacting).OrderBy(mark => mark.At)]);
    }

    private static RelayLeg Closed(RelayLeg leg, DateTimeOffset to, IReadOnlyList<RelayMark> marks)
    {
        var within = marks.Where(mark => mark.At >= leg.From - Slack && mark.At <= to + Slack).ToList();

        return leg with
        {
            To = to < leg.From ? leg.From : to,
            Ending = within.LastOrDefault(mark => mark.Connection == leg.Connection && mark.Kind is RelayMarkKind.WindingDown or RelayMarkKind.Capped) is { } ending ? ending : Option<RelayMark>.None,
            Compacting = leg.Kind == RelayLegKind.Waiting && within.Any(mark => mark.Kind == RelayMarkKind.Compacting),
        };
    }

    private static RelayLeg Last(RelayLeg leg, JobStatus status) => leg.Kind switch
    {
        RelayLegKind.Waiting when status == JobStatus.NeedsHelp => leg,
        _ when status is JobStatus.Running or JobStatus.Preparing or JobStatus.Checking => leg with { Kind = RelayLegKind.Running },
        _ => leg with { Kind = RelayLegKind.Ran },
    };

    private static List<Switch> Switches(IReadOnlyList<HandoffRecord> handoffs, IReadOnlyList<RelayMark> marks, Option<ResetWait> wait)
    {
        var switches = handoffs.Select(handoff => new Switch(handoff.At, handoff.To, RelayLegKind.Ran, handoff, Option<DateTimeOffset>.None))
            .Concat(marks.Where(mark => mark.Kind == RelayMarkKind.Waiting).Select(mark => new Switch(mark.At, mark.Connection, RelayLegKind.Waiting, Option<HandoffRecord>.None, mark.Until)))
            .Concat(marks.Where(mark => mark.Kind == RelayMarkKind.Resumed).Select(mark => new Switch(mark.At, mark.Connection, RelayLegKind.Ran, Option<HandoffRecord>.None, Option<DateTimeOffset>.None)))
            .ToList();

        foreach (var waiting in wait.Match<ResetWait[]>(found => [found], () => []).Where(found => !found.HasReset && !switches.Any(change => change.Kind == RelayLegKind.Waiting && change.At >= found.Since - Slack)))
        {
            switches.Add(new Switch(waiting.Since, waiting.Connection, RelayLegKind.Waiting, Option<HandoffRecord>.None, waiting.ResumesAt));
        }

        return [.. switches.OrderBy(change => change.At)];
    }

    private sealed record Switch(DateTimeOffset At, ConnectionName Connection, RelayLegKind Kind, Option<HandoffRecord> Handoff, Option<DateTimeOffset> ResumesAt);
}
