using Avala.Server.Activity.Contracts.Ingestion;
using Avala.Server.Activity.Domain;

namespace Avala.Server.Activity.Infrastructure.Ingestion;

internal sealed class IngestionBatch
{
    private readonly List<Guid> accepted = [];

    private readonly List<Guid> duplicates = [];

    private readonly List<RejectedEvent> rejected = [];

    private readonly List<(IIncomingEvent Event, EventTime At)> admitted = [];

    private readonly IReadOnlyList<IIncomingEvent> events;

    private readonly DateTimeOffset receivedAt;

    public IngestionBatch(IReadOnlyList<IIncomingEvent> events, DateTimeOffset receivedAt)
    {
        HashSet<Guid> seen = [];
        this.events = [.. events.Where(incoming => seen.Add(incoming.Id))];
        this.receivedAt = receivedAt;
    }

    public IReadOnlyList<Guid> Ids => [.. events.Select(incoming => incoming.Id)];

    public IReadOnlyList<Guid> JobIds => [.. admitted.Select(entry => EventApplication.JobOf(entry.Event)).OfType<Guid>().Distinct()];

    public (IReadOnlyList<Guid> Jobs, IReadOnlyList<DateTimeOffset> Quarters) UsageKeys
    {
        get
        {
            var usage = admitted.Where(entry => entry.Event is UsageReportedEvent).ToList();

            return (
                [.. usage.Select(entry => ((UsageReportedEvent)entry.Event).Job ?? QuarterUsage.OutsideAnyJob).Distinct()],
                [.. usage.Select(entry => UsageQuarter.Of(entry.At)).Distinct()]);
        }
    }

    public IEnumerable<(IIncomingEvent Event, EventTime At)> InOrder => admitted.OrderBy(entry => entry.At.At);

    public int Clamped => admitted.Count(entry => entry.At.Clamped);

    public void Sort(IReadOnlySet<Guid> known)
    {
        foreach (var incoming in events)
        {
            var time = EventTime.Create(incoming.OccurredAt, receivedAt);

            if (known.Contains(incoming.Id))
            {
                duplicates.Add(incoming.Id);
            }
            else if (time.Refusal is { } refusal)
            {
                rejected.Add(new RejectedEvent(incoming.Id, refusal));
            }
            else
            {
                admitted.Add((incoming, time.Value));
            }
        }
    }

    public void Accept(Guid id) => accepted.Add(id);

    public void Reject(Guid id, string reason) => rejected.Add(new RejectedEvent(id, reason));

    public IngestionReceipt Receipt() => new(accepted, duplicates, rejected);
}
