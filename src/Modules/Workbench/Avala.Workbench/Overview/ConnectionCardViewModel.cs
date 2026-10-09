using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Fleet;
using Avala.Workbench.Presenting;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Usage;

namespace Avala.Workbench.Overview;

internal sealed class ConnectionCardViewModel(ConnectionState connection)
{
    public ConnectionName Connection { get; } = connection.Name;

    public string Name { get; } = connection.Name.Value;

    public string Provider { get; } = connection.Provider;

    public string Account { get; } = connection.Account.Match(account => account.Label, () => "account not reported yet");

    public bool IsDefault { get; } = connection.IsDefault;

    public string Cost { get; } = connection.Usage.Match(usage => Amounts.Costs(usage.Costs), () => "no usage yet");

    public IReadOnlyList<LimitViewModel> Limits { get; } =
        connection.Usage.Match(usage => usage.Limits.Select(limit => new LimitViewModel(limit, Option<double>.None)).ToList(), () => []);

    public IReadOnlyList<AgentViewModel> Agents { get; } = [.. connection.Agents.Select(job => new AgentViewModel(job))];
}

internal sealed class AgentViewModel(BoardJob job)
{
    public JobId Job { get; } = job.Job;

    public string Title { get; } = FactPhrases.Title(job.Summary.Instruction);

    public JobStatus Status { get; } = job.Status;

    public string Fact { get; } = FactPhrases.Of(job.Fact);
}
