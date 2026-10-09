using Avala.Agents.Contracts.Connections;
using Avala.Observability.Contracts;

namespace Avala.Delegation.Delegating;

internal sealed class ConnectionGauge(IUsage usage, TimeProvider clock)
{
    public double Used(ConnectionName connection)
    {
        var now = clock.GetUtcNow();

        return usage.ByConnection()
            .Where(used => used.Connection == connection)
            .SelectMany(used => used.Usage.Limits)
            .Where(limit => limit.ResetsAt.Match(resets => resets > now, () => true))
            .Select(limit => limit.UsedFraction)
            .DefaultIfEmpty(0)
            .Max();
    }
}
