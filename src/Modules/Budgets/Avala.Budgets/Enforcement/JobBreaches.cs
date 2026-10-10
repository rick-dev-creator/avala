using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Caps;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;

namespace Avala.Budgets.Enforcement;

internal sealed class JobBreaches(BudgetBook book, IUsage usage)
{
    public Lineage Tree => new(book.Carves, Spent, book.Ended);

    public Option<BudgetBreach> Of(JobId job, SessionId session, IReadOnlyList<UsageLimit> limits, long memoryBytes) =>
        book.Budgeted(session).Bind(budgeted => (budgeted.Budget with { Caps = budgeted.Budget.Caps.Within(book.CarveOf(job)) }).BreachBy(
            Tree.CommittedBy(job),
            limits,
            memoryBytes));

    public Option<BudgetBreach> Overspent(JobId job, SessionId session) => Overspending(Of(job, session, [], 0));

    public static Option<BudgetBreach> Overspending(Option<BudgetBreach> breach) =>
        breach.Bind(found => found.Reason == HoldReason.BudgetExceeded ? found : Option<BudgetBreach>.None);

    public IReadOnlyList<UsageLimit> LimitsOf(ConnectionName connection, DateTimeOffset now) =>
        [
            .. usage.ByConnection()
                .Where(used => used.Connection == connection)
                .SelectMany(used => used.Usage.Limits)
                .Where(limit => limit.ResetsAt.Match(resets => resets > now, () => true)),
        ];

    private Commitment Spent(JobId job) => usage.OfJob(job).Match(Commitment.Of, () => Commitment.Nothing);
}
