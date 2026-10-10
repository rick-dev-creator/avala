using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Handoffs.Policy;

internal sealed record Availability(Option<UsageLimit> Window, Option<DateTimeOffset> At)
{
    public static Availability Now { get; } = new(Option<UsageLimit>.None, Option<DateTimeOffset>.None);

    public bool IsNow => Window.IsNone;
}

internal static class Capacity
{
    public static IReadOnlyList<UsageLimit> Counting(IEnumerable<UsageLimit> limits, DateTimeOffset now) =>
        [.. limits.Where(limit => limit.ResetsAt.Match(resets => resets > now, () => true))];

    public static Availability Of(IEnumerable<UsageLimit> limits, double threshold, DateTimeOffset now)
    {
        var over = Counting(limits, now).Where(limit => limit.UsedFraction >= threshold).OrderByDescending(limit => limit.UsedFraction).ToList();

        if (over.Count == 0)
        {
            return Availability.Now;
        }

        var at = over.Exists(limit => limit.ResetsAt.IsNone)
            ? Option<DateTimeOffset>.None
            : over.Max(limit => limit.ResetsAt.Match(resets => resets, () => DateTimeOffset.MinValue));

        return new Availability(over[0], at);
    }

    public static Availability Held(IEnumerable<UsageLimit> limits, double threshold, DateTimeOffset now)
    {
        var counting = Counting(limits, now);
        var mostUsed = counting.Count == 0 ? 0 : counting.Max(limit => limit.UsedFraction);

        return mostUsed <= 0 ? Availability.Now : Of(counting, Math.Min(threshold, mostUsed), now);
    }

    public static CandidateCapacity Measure(ConnectionName connection, IEnumerable<UsageLimit> limits, double threshold, DateTimeOffset now)
    {
        var mostUsed = Counting(limits, now).OrderByDescending(limit => limit.UsedFraction).Select(Option<UsageLimit>.Some).FirstOrDefault();
        var used = mostUsed.Match(limit => limit.UsedFraction, () => 0);

        return new CandidateCapacity(connection, used, mostUsed, threshold, used < threshold);
    }
}
