using Avala.Budgets.Capacity;
using Avala.Budgets.Enforcement;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;

namespace Avala.Budgets.Routing;

internal sealed class CapacitySelector(IBudgetFiles files, IUsage usage, TimeProvider clock) : IConnectionSelector
{
    public async ValueTask<Option<ConnectionChoice>> ChooseAsync(ConnectionQuestion question, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var readings = usage.ByConnection();
        var compared = new List<CandidateCapacity>();

        foreach (var candidate in question.Candidates)
        {
            var budget = await files.ReadAsync(question.Worktree, candidate, cancellationToken);
            var threshold = budget.Caps.Match(caps => caps.Bind(declared => declared.HoldAtLimit), _ => Option<double>.None);
            compared.Add(CapacityPolicy.Measure(
                candidate,
                readings.Where(used => used.Connection == candidate).SelectMany(used => used.Usage.Limits),
                threshold.Match(declared => declared, () => CapacityPolicy.SpentWindow),
                now));
        }

        return CapacityPolicy.Choose(compared, now);
    }
}
