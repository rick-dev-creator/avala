using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Budgets.Capacity;

internal static class CapacityPolicy
{
    public const double SpentWindow = 1;

    public static CandidateCapacity Measure(ConnectionName connection, IEnumerable<UsageLimit> limits, double threshold, DateTimeOffset now)
    {
        var mostUsed = limits
            .Where(limit => limit.ResetsAt.Match(resets => resets > now, () => true))
            .OrderByDescending(limit => limit.UsedFraction)
            .Select(Option<UsageLimit>.Some)
            .FirstOrDefault();
        var used = mostUsed.Match(limit => limit.UsedFraction, () => 0);

        return new CandidateCapacity(connection, used, mostUsed, threshold, used < threshold);
    }

    public static Option<ConnectionChoice> Choose(IReadOnlyList<CandidateCapacity> compared, DateTimeOffset at)
    {
        var available = compared.Where(candidate => candidate.Available).ToList();
        var pool = available.Count > 0 ? available : compared;

        return pool.MinBy(candidate => candidate.Used) is { } chosen
            ? new ConnectionChoice(chosen.Connection, available.Count > 0 ? ChoiceReason.MostCapacity : ChoiceReason.AllAtLimit, compared, at)
            : Option<ConnectionChoice>.None;
    }
}
