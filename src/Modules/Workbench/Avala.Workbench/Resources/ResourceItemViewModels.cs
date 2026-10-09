using System.Globalization;
using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Workbench.Presenting;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Upkeep;

namespace Avala.Workbench.Resources;

internal interface IAgentTreeViewModel
{
    string Job { get; }

    string Connection { get; }

    string Provider { get; }

    int Processes { get; }

    string Memory { get; }

    string Cpu { get; }

    string Ports { get; }
}

internal interface IOrphanViewModel
{
    Option<JobId> Job { get; }

    IReadOnlyList<string> Processes { get; }

    bool IsLeftRunning { get; }

    bool CanReap { get; }

    string Disposal { get; }

    string At { get; }
}

internal interface IStaleWorktreeViewModel
{
    string Path { get; }

    string Reason { get; }
}

internal sealed class AgentTreeViewModel(TreeState state) : IAgentTreeViewModel
{
    public string Job { get; } = state.Job.Match(job => FactPhrases.Title(job.Summary.Instruction), () => "no job");

    public string Connection { get; } = state.Tree.Connection.Match(name => name.Value, () => string.Empty);

    public string Provider { get; } = state.Tree.Provider.Match(provider => provider, () => string.Empty);

    public int Processes { get; } = state.Tree.Processes.Count;

    public string Memory { get; } = Amounts.Megabytes(state.Tree.Processes.Sum(process => process.MemoryBytes));

    public string Cpu { get; } = Amounts.Percent(state.Tree.CpuLoad);

    public string Ports { get; } = string.Join(", ", state.Tree.Processes.SelectMany(process => process.Ports).Select(port => port.ToString(CultureInfo.InvariantCulture)));
}

internal sealed class OrphanViewModel(OrphanReport report) : IOrphanViewModel
{
    public Option<JobId> Job { get; } = report.Job;

    public IReadOnlyList<string> Processes { get; } =
        [.. report.Processes.Select(process => string.Create(CultureInfo.InvariantCulture, $"{process.Name} ({process.Id})"))];

    public bool IsLeftRunning { get; } = report.Disposal == OrphanDisposal.LeftRunning;

    public bool CanReap { get; } = report.Disposal == OrphanDisposal.LeftRunning && report.Job.IsSome;

    public string Disposal { get; } = report.Disposal == OrphanDisposal.Killed
        ? report.Survivors.Count == 0 ? "killed" : string.Create(CultureInfo.InvariantCulture, $"killed, {report.Survivors.Count} survived")
        : "left running";

    public string At { get; } = Amounts.Time(report.At);
}

internal sealed class StaleWorktreeViewModel(string path, string reason) : IStaleWorktreeViewModel
{
    public string Path { get; } = path;

    public string Reason { get; } = reason;
}
