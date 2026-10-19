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
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Overview;

internal interface IDelegationNodeViewModel
{
    JobId Job { get; }

    string Title { get; }

    int Depth { get; }

    JobStatus Status { get; }

    string StateText { get; }

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

    ContextGlance Context { get; }

    string Tokens { get; }

    string Spent { get; }

    IReadOnlyList<IRecentActionViewModel> Recent { get; }

    string Doing { get; }

    int SubagentCount { get; }

    string Agents { get; }

    bool NeedsYou { get; }

    bool IsSettled { get; }

    bool IsOpen { get; }

    IRelayCommand ToggleCommand { get; }

    IPeekViewModel? Peek { get; }

    bool IsPeeking { get; }

    IRelayCommand PeekCommand { get; }
}

internal interface IDelegationRefusalViewModel
{
    string Instruction { get; }

    string Reason { get; }
}

[INotifyPropertyChanged]
internal sealed partial class DelegationNodeViewModel : IDelegationNodeViewModel
{
    private readonly StatusDotViewModel dot = new(default);

    public DelegationNodeViewModel(DelegationNode node)
    {
        Job = node.Job.Job;
        Show(node);
    }

    public JobId Job { get; }

    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial int Depth { get; private set; }

    [ObservableProperty]
    public partial JobStatus Status { get; private set; }

    [ObservableProperty]
    public partial string StateText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Connection { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Harness { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Activity { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Role { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Spend { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasCarve { get; private set; }

    [ObservableProperty]
    public partial double SpentShare { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(State))]
    public partial StatusKind Kind { get; private set; }

    public IStatusDotViewModel Dot => dot;

    public string State => OverviewPhrases.State(Kind);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSubagents))]
    public partial IReadOnlyList<ISubagentViewModel> Subagents { get; private set; } = [];

    public bool HasSubagents => Subagents.Count > 0;

    [ObservableProperty]
    public partial bool IsViewing { get; set; }

    public IRelayCommand<JobId> OpenCommand { get; init; } = OverviewPhrases.Idle;

    public event EventHandler? Toggled;

    public event EventHandler? Peeked;

    public Option<BoardJob> Seen { get; private set; }

    [ObservableProperty]
    public partial IPeekViewModel? Peek { get; set; }

    [ObservableProperty]
    public partial bool IsPeeking { get; private set; }

    [ObservableProperty]
    public partial ContextGlance Context { get; private set; } = ContextGlance.None;

    [ObservableProperty]
    public partial string Tokens { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Spent { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<IRecentActionViewModel> Recent { get; private set; } = [];

    [ObservableProperty]
    public partial string Doing { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Agents))]
    public partial int SubagentCount { get; private set; }

    public string Agents => OverviewPhrases.Count(SubagentCount, "agent");

    [ObservableProperty]
    public partial bool NeedsYou { get; private set; }

    [ObservableProperty]
    public partial bool IsSettled { get; private set; }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [RelayCommand]
    private void Toggle()
    {
        IsOpen = !IsOpen;
        IsPeeking = IsPeeking && IsOpen;
        Toggled?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void PeekNow()
    {
        IsPeeking = !IsPeeking;
        Peeked?.Invoke(this, EventArgs.Empty);
    }

    IRelayCommand IDelegationNodeViewModel.PeekCommand => PeekNowCommand;

    partial void OnIsOpenChanged(bool value) => IsPeeking = IsPeeking && value;

    public void Show(DelegationNode node)
    {
        Seen = node.Seen;
        Title = FactPhrases.Title(node.Job.Instruction);
        Depth = node.Depth;
        Status = node.Job.Status;
        Connection = node.Job.Connection
            .Match(name => name.Value, () => node.Delegation.Bind(record => record.Connection).Match(name => name.Value, () => "not started"));
        Harness = node.Spend.Session.Match(seen => seen.Provider.Name, () => string.Empty);
        Activity = node.Report.Match(
            report => DelegationPhrases.Outcome(report.Outcome),
            () => node.Activity.Match(FactPhrases.Of, () => DelegationPhrases.Status(node.Job.Status)));
        Role = node.Delegation.Match(record => DelegationPhrases.Role(record.Role), () => string.Empty);
        Spend = Spending(node.Spend);
        HasCarve = Carved(node.Spend).IsSome;
        SpentShare = Carved(node.Spend).Match(cost => Math.Clamp(Shares.Of(node.Spend.Spent, cost), 0, 1), () => 0d);
        dot.Kind = node.Seen.Match(FactPhrases.Dot, () => OverviewPhrases.Dot(node.Job.Status));
        Kind = dot.Kind;
        Subagents = SubagentViewModel.Kept(Subagents, SubagentViewModel.Of(node.Seen));
        Context = AgentGlance.Context(node.Seen);
        Tokens = AgentGlance.TokensText(AgentGlance.Tokens(node.Spend));
        Spent = node.Spend.Spent.Count == 0 ? string.Empty : Amounts.Costs(node.Spend.Spent);
        var recent = AgentGlance.Recent(node.Seen);
        Recent = AgentGlance.SameRecent(Recent, recent) ? Recent : recent;
        Doing = node.Report.IsSome || recent.Count == 0 ? Activity : recent[0].Text;
        SubagentCount = SubagentViewModel.Count(node.Seen);
        NeedsYou = dot.Kind is StatusKind.NeedsYou or StatusKind.Held;
        StateText = SubagentStates.Text(SubagentLifecycle.Of(node.Job.Status, node.Job.Hold, dot.Kind is StatusKind.NeedsYou));
        IsSettled = TreeOrder.IsSettled(node);
    }

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
