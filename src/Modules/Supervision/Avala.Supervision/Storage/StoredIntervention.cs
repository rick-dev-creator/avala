using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Supervision.Contracts;

namespace Avala.Supervision.Storage;

internal sealed class StoredIntervention
{
    public int Key { get; init; }

    public Guid Job { get; init; }

    public Guid Session { get; init; }

    public string Reason { get; init; } = string.Empty;

    public string Halt { get; init; } = string.Empty;

    public long Silent { get; init; }

    public long Window { get; init; }

    public long At { get; init; }

    public static StoredIntervention Of(SupervisionIntervention intervention) => new()
    {
        Job = intervention.Hold.Job.Value,
        Session = intervention.Hold.Session.Value,
        Reason = intervention.Hold.Reason.ToString(),
        Halt = intervention.Hold.Halt.ToString(),
        Silent = intervention.Silence.Silent.Ticks,
        Window = intervention.Silence.Window.Ticks,
        At = intervention.At.UtcTicks,
    };

    public SupervisionIntervention Intervention() => new(
        new JobHold(new JobId(Job), new SessionId(Session), Enum.Parse<HoldReason>(Reason), Enum.Parse<SessionHalt>(Halt)),
        new SilenceMeasure(TimeSpan.FromTicks(Silent), TimeSpan.FromTicks(Window)),
        new DateTimeOffset(At, TimeSpan.Zero));
}
