namespace Avala.Server.Activity.Domain;

internal sealed record EventTime(DateTimeOffset At, bool Clamped)
{
    public static DateTimeOffset Earliest { get; } = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static Admission<EventTime> Create(DateTimeOffset occurredAt, DateTimeOffset receivedAt)
    {
        var instant = occurredAt.ToUniversalTime();
        var reception = receivedAt.ToUniversalTime();

        return instant < Earliest ? Admission.Refuse<EventTime>("the time is before 2025")
            : instant > reception ? Admission.Admit(new EventTime(reception, Clamped: true))
            : Admission.Admit(new EventTime(instant, Clamped: false));
    }
}
