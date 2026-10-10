using System.Collections.Immutable;

namespace Avala.Triggers.Webhooks;

internal sealed record ReplayGuard(ImmutableDictionary<string, DateTimeOffset> Accepted)
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(300);

    public static ReplayGuard Empty { get; } = new(ImmutableDictionary<string, DateTimeOffset>.Empty);

    public static bool Fresh(DateTimeOffset timestamp, DateTimeOffset now) => (now - timestamp).Duration() <= Window;

    public bool Seen(string trigger, string nonce, DateTimeOffset now) =>
        Accepted.TryGetValue(Key(trigger, nonce), out var at) && now - at <= Window * 2;

    public ReplayGuard Accept(string trigger, string nonce, DateTimeOffset now) =>
        new(Accepted.RemoveRange(Accepted.Where(entry => now - entry.Value > Window * 2).Select(entry => entry.Key)).SetItem(Key(trigger, nonce), now));

    private static string Key(string trigger, string nonce) => $"{trigger}\n{nonce}";
}

internal sealed record RateWindow(ImmutableDictionary<string, ImmutableList<DateTimeOffset>> Requests)
{
    public static readonly TimeSpan Span = TimeSpan.FromHours(1);

    public static RateWindow Empty { get; } = new(ImmutableDictionary<string, ImmutableList<DateTimeOffset>>.Empty);

    public (RateWindow Window, TimeSpan RetryAfter) Admit(string trigger, DateTimeOffset now, int perHour)
    {
        var recent = Requests.GetValueOrDefault(trigger, []).RemoveAll(at => now - at >= Span);

        return recent.Count >= perHour
            ? (new RateWindow(Requests.SetItem(trigger, recent)), recent[0] + Span - now)
            : (new RateWindow(Requests.SetItem(trigger, recent.Add(now))), TimeSpan.Zero);
    }
}
