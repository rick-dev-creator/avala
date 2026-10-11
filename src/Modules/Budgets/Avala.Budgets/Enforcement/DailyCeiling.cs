using Avala.Agents.Contracts.Connections;
using Avala.Budgets.Admission;
using Avala.Budgets.Caps;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;

namespace Avala.Budgets.Enforcement;

internal sealed class DailyCeiling(IMachineBudgetFile machine, IUsageHistory history, TimeProvider clock)
{
    public async Task<IReadOnlyList<(JobId Job, BudgetBreach Breach)>> BreachesAsync(
        IReadOnlyList<(JobId Job, ConnectionName Connection)> running,
        CancellationToken cancellationToken)
    {
        if (running.Count == 0)
        {
            return [];
        }

        var budget = await machine.LoadAsync(cancellationToken);

        if (budget.Declaration.IsNone && !budget.Ceiled)
        {
            return [];
        }

        var today = budget.Declaration.IsSome ? Option<UsagePeriod>.None : await TodayAsync(cancellationToken);

        return
        [
            .. running.SelectMany(tied => (budget.Declaration.IsSome ? budget.Declaration : today.Bind(spent => budget.BreachBy(spent, tied.Connection)))
                .Match<(JobId, BudgetBreach)[]>(found => [(tied.Job, found)], () => [])),
        ];
    }

    private async Task<Option<UsagePeriod>> TodayAsync(CancellationToken cancellationToken)
    {
        var zone = clock.LocalTimeZone;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);

        return await history.DailyAsync(today, today, zone, cancellationToken) is [var period, ..] ? period : Option<UsagePeriod>.None;
    }
}
