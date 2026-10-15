using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Components.Status;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Fleet;
using Avala.Workbench.Presenting;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Spending;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Overview;

internal interface IDelegationNodeViewModel
{
    JobId Job { get; }

    string Title { get; }

    int Depth { get; }

    JobStatus Status { get; }

    string Connection { get; }

    string Harness { get; }

    string Activity { get; }

    string Role { get; }

    string Spend { get; }

    bool HasCarve { get; }

    double SpentShare { get; }

    string State { get; }

    IStatusDotViewModel Dot { get; }

    IReadOnlyList<ISubagentViewModel> Subagents { get; }

    bool HasSubagents { get; }

    bool IsViewing { get; }

    IRelayCommand<JobId> OpenCommand { get; }
}

internal interface IDelegationRefusalViewModel
{
    string Instruction { get; }

    string Reason { get; }
}

internal sealed class DelegationNodeViewModel(DelegationNode node) : IDelegationNodeViewModel
{
    public JobId Job { get; } = node.Job.Job;

    public string Title { get; } = FactPhrases.Title(node.Job.Instruction);

    public int Depth { get; } = node.Depth;

    public JobStatus Status { get; } = node.Job.Status;

    public string Connection { get; } = node.Job.Connection
        .Match(name => name.Value, () => node.Delegation.Bind(record => record.Connection).Match(name => name.Value, () => "not started"));

    public string Harness { get; } = node.Spend.Session.Match(seen => seen.Provider.Name, () => string.Empty);

    public string Activity { get; } = node.Report.Match(
        report => DelegationPhrases.Outcome(report.Outcome),
        () => node.Activity.Match(FactPhrases.Of, () => DelegationPhrases.Status(node.Job.Status)));

    public string Role { get; } = node.Delegation.Match(record => DelegationPhrases.Role(record.Role), () => string.Empty);

    public string Spend { get; } = Spending(node.Spend);

    public bool HasCarve { get; } = Carved(node.Spend).IsSome;

    public double SpentShare { get; } = Carved(node.Spend).Match(cost => Math.Clamp(Shares.Of(node.Spend.Spent, cost), 0, 1), () => 0d);

    public IStatusDotViewModel Dot { get; } = new StatusDotViewModel(node.Seen.Match(FactPhrases.Dot, () => OverviewPhrases.Dot(node.Job.Status)));

    public string State => OverviewPhrases.State(Dot.Kind);

    public IReadOnlyList<ISubagentViewModel> Subagents { get; } = SubagentViewModel.Of(node.Seen);

    public bool HasSubagents => Subagents.Count > 0;

    public bool IsViewing { get; init; }

    public IRelayCommand<JobId> OpenCommand { get; init; } = OverviewPhrases.Idle;

    private static Option<IReadOnlyList<Cost>> Carved(JobSpend spend) =>
        spend.Carve.Bind(carve => carve.Cost.Count == 0 ? Option<IReadOnlyList<Cost>>.None : Option<IReadOnlyList<Cost>>.Some(carve.Cost));

    private static string Spending(JobSpend spend) =>
        Carved(spend).Match(
            cost => $"{Amounts.Costs(spend.Spent)} of {Amounts.Costs(cost)}",
            () => spend.Spent.Count == 0 ? "no spend yet" : $"{Amounts.Costs(spend.Spent)} spent");
}

internal sealed class DelegationRefusalViewModel(DelegationRecord record) : IDelegationRefusalViewModel
{
    public string Instruction { get; } = FactPhrases.Title(record.Instruction);

    public string Reason { get; } = record.Refusal.Match(DelegationPhrases.Refusal, () => string.Empty);
}

internal static partial class OverviewPhrases
{
    public static IRelayCommand<JobId> Idle { get; } = new RelayCommand<JobId>(_ => { });

    public static StatusKind Dot(JobStatus status) => status switch
    {
        JobStatus.Checking => StatusKind.Checking,
        JobStatus.NeedsHelp => StatusKind.NeedsYou,
        JobStatus.AwaitingReview => StatusKind.ReadyForReview,
        JobStatus.Approved or JobStatus.Discarded => StatusKind.Done,
        JobStatus.Failed => StatusKind.Failed,
        _ => StatusKind.Working,
    };

    public static string Repository(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        var slash = trimmed.LastIndexOfAny(['/', '\\']);

        return slash < 0 ? trimmed : trimmed[(slash + 1)..];
    }

    public static string Waiting(int children) =>
        children == 0 ? "no sub-agent at work" : string.Create(CultureInfo.InvariantCulture, $"waiting on {Count(children, "sub-agent")}");
}
