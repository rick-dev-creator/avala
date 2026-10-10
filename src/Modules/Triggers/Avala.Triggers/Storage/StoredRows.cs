using Avala.Sdk;
using Avala.Storage;
using Avala.Triggers.Contracts;
using Avala.Triggers.Scheduling;

namespace Avala.Triggers.Storage;

internal sealed class StoredSchedule
{
    private const long Absent = -1;

    public string Trigger { get; init; } = string.Empty;

    public long Since { get; set; }

    public long Anchor { get; set; }

    public long LastFired { get; set; }

    public int Enabled { get; set; }

    public static StoredSchedule Of(ScheduleState state) => new()
    {
        Trigger = state.Trigger,
        Since = state.Since.UtcTicks,
        Anchor = Ticks(state.Anchor),
        LastFired = Ticks(state.LastFired),
        Enabled = state.Enabled.Match(enabled => enabled ? 1 : 0, () => -1),
    };

    public ScheduleState Read() => new(Trigger, At(Since))
    {
        Anchor = Moment(Anchor),
        LastFired = Moment(LastFired),
        Enabled = Enabled < 0 ? Option<bool>.None : Enabled == 1,
    };

    private static long Ticks(Option<DateTimeOffset> at) => at.Match(moment => moment.UtcTicks, () => Absent);

    private static Option<DateTimeOffset> Moment(long ticks) => ticks == Absent ? Option<DateTimeOffset>.None : At(ticks);

    private static DateTimeOffset At(long ticks) => new(ticks, TimeSpan.Zero);
}

internal sealed class StoredRun
{
    public int Key { get; init; }

    public Guid Run { get; init; }

    public string Trigger { get; init; } = string.Empty;

    public long At { get; init; }

    public string Fact { get; set; } = string.Empty;

    public static StoredRun Of(TriggerRun run) => new() { Run = run.Id, Trigger = run.Trigger.Key, At = run.At.UtcTicks, Fact = StoredJson.Write(run) };

    public TriggerRun Read() => StoredJson.Read<TriggerRun>(Fact);
}

internal sealed class StoredDelivery
{
    public int Key { get; init; }

    public long At { get; init; }

    public string Fact { get; init; } = string.Empty;

    public static StoredDelivery Of(WebhookDelivery delivery) => new() { At = delivery.At.UtcTicks, Fact = StoredJson.Write(delivery) };

    public WebhookDelivery Read() => StoredJson.Read<WebhookDelivery>(Fact);
}
