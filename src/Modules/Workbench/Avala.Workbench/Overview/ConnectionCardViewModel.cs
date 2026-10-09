using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Fleet;
using Avala.Workbench.Presenting;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Usage;

namespace Avala.Workbench.Overview;

internal interface IConnectionCardViewModel
{
    string Name { get; }

    string Provider { get; }

    string Account { get; }

    bool IsDefault { get; }

    string Cost { get; }

    IReadOnlyList<ILimitViewModel> Limits { get; }

    IReadOnlyList<IAgentViewModel> Agents { get; }
}

internal interface IAgentViewModel
{
    JobId Job { get; }

    string Title { get; }

    JobStatus Status { get; }

    string Fact { get; }
}

internal sealed class ConnectionCardViewModel(ConnectionState connection) : IConnectionCardViewModel
{
    public ConnectionName Connection { get; } = connection.Name;

    public string Name { get; } = connection.Name.Value;

    public string Provider { get; } = connection.Provider;

    public string Account { get; } = connection.Account.Match(account => account.Label, () => "account not reported yet");

    public bool IsDefault { get; } = connection.IsDefault;

    public string Cost { get; } = connection.Usage.Match(usage => Amounts.Costs(usage.Costs), () => "no usage yet");

    public IReadOnlyList<ILimitViewModel> Limits { get; } =
        connection.Usage.Match<IReadOnlyList<ILimitViewModel>>(usage => [.. usage.Limits.Select(limit => new LimitViewModel(limit, Option<double>.None))], () => []);

    public IReadOnlyList<IAgentViewModel> Agents { get; } = [.. connection.Agents.Select(job => new AgentViewModel(job))];
}

internal sealed class AgentViewModel(BoardJob job) : IAgentViewModel
{
    public JobId Job { get; } = job.Job;

    public string Title { get; } = FactPhrases.Title(job.Summary.Instruction);

    public JobStatus Status { get; } = job.Status;

    public string Fact { get; } = FactPhrases.Of(job.Fact);
}
