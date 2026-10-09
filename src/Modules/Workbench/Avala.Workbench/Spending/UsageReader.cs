using Avala.Budgets.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;

namespace Avala.Workbench.Spending;

internal sealed record ConnectionSpend(ConnectionUsage Usage, Option<BudgetCaps> Caps)
{
    public IReadOnlyList<LimitReading> Limits { get; init; } = [];
}

internal sealed record JobCost(BoardJob Job, JobSpend Spend, IReadOnlyList<JobIntervention> Interventions);

internal sealed record SpendingState(IReadOnlyList<ConnectionSpend> Connections, IReadOnlyList<JobCost> Jobs, IReadOnlyList<JobIntervention> Interventions);

internal sealed class UsageReader(LimitReadings readings, JobSpending spending, JobBoard board)
{
    public SpendingState Read()
    {
        var jobs = board.Jobs.Values
            .OrderByDescending(job => job.Summary.Submitted)
            .Select(job => new JobCost(job, spending.Of(job.Job), spending.InterventionsOf(job.Job)))
            .Where(cost => cost.Spend.Usage.IsSome || cost.Interventions.Count > 0)
            .ToList();

        return new SpendingState(
            [
                .. readings.ByConnection().Select(connection => new ConnectionSpend(connection, spending.CapsOn(connection.Connection))
                {
                    Limits = readings.Judged(connection.Usage.Limits),
                }),
            ],
            jobs,
            [.. jobs.SelectMany(cost => cost.Interventions).OrderByDescending(intervention => intervention.At)]);
    }
}
