using System.Collections.Immutable;
using Avala.Notifications.Contracts;
using Avala.Sdk;

namespace Avala.Notifications.Pacing;

internal sealed record Circumstances(bool Away, DateTimeOffset NextLook, Option<DateTimeOffset> QuietUntil, ImmutableHashSet<string> Sent, bool Capped)
{
    public static TimeSpan AwayAfter { get; } = TimeSpan.FromMinutes(2);

    public const int MostPerHour = 20;
}

internal sealed record Settlement(Pending Entry, NotificationOutcome Outcome);

internal sealed record Verdicts(IReadOnlyList<Pending> Send, IReadOnlyList<Pending> Later, IReadOnlyList<Pending> Held, IReadOnlyList<Settlement> Settled);

internal static class Triage
{
    public static Verdicts Of(IReadOnlyList<Pending> ripe, Circumstances now)
    {
        var send = new List<Pending>();
        var later = new List<Pending>();
        var held = new List<Pending>();
        var settled = new List<Settlement>();

        foreach (var entry in ripe)
        {
            var urgent = entry.Notice.Severity == Severity.Urgent;

            switch (entry)
            {
                case var _ when now.Sent.Contains(entry.Notice.Subject):
                    settled.Add(new Settlement(entry, NotificationOutcome.Duplicate));
                    break;
                case var _ when !now.Away && urgent:
                    later.Add(entry with { Due = now.NextLook });
                    break;
                case var _ when !now.Away:
                    settled.Add(new Settlement(entry, NotificationOutcome.Present));
                    break;
                case var _ when now.QuietUntil.IsSome && !urgent:
                    held.Add(entry);
                    break;
                case var _ when now.Capped:
                    settled.Add(new Settlement(entry, NotificationOutcome.Capped));
                    break;
                default:
                    send.Add(entry);
                    break;
            }
        }

        return new Verdicts(send, later, held, settled);
    }
}
