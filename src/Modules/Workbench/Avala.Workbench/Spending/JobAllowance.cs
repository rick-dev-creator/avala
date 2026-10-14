using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;

namespace Avala.Workbench.Spending;

internal sealed class JobAllowance(LimitReadings readings, JobSpending spending, JobBoard board, WindowWatch watch)
{
    public Option<ConnectionSpend> Of(JobId job) =>
        Connection(job)
            .Bind(name => readings.ByConnection().FirstOrDefault(usage => usage.Connection == name).ToOption())
            .Map(usage => new ConnectionSpend(usage, spending.CapsOn(usage.Connection)) { Limits = watch.Watched(usage.Connection, readings.Judged(usage.Usage.Limits)) });

    private Option<ConnectionName> Connection(JobId job) =>
        spending.Of(job).Session.Match(
            seen => Option<ConnectionName>.Some(seen.Connection),
            () => board.Find(job).Bind(found => found.Summary.Connection));
}
