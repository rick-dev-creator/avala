using Avala.Components.Graphs;
using Avala.Delegation.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Overview;

internal interface IAgentBranch
{
    IBranchPlaceViewModel Place { get; }
}

internal interface IBranchPlaceViewModel
{
    string Key { get; }

    int Depth { get; }

    BranchKind Kind { get; }

    string KindText { get; }

    SubagentState State { get; }

    string StateText { get; }

    EdgeKind Edge { get; }

    bool IsWorking { get; }

    bool IsWaiting { get; }

    bool IsAlert { get; }

    bool IsSettled { get; }

    int Branches { get; }

    bool HasBranches { get; }

    bool IsFolded { get; }

    HarnessMark Harness { get; }

    bool IsCrossHarness { get; }

    bool RunsInside { get; }

    IRelayCommand FoldCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class BranchPlaceViewModel(string key, Action<string> fold) : IBranchPlaceViewModel
{
    public string Key { get; } = key;

    [ObservableProperty]
    public partial int Depth { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KindText))]
    [NotifyPropertyChangedFor(nameof(IsCrossHarness), nameof(RunsInside))]
    public partial BranchKind Kind { get; private set; }

    public bool IsCrossHarness => Kind == BranchKind.CrossHarness;

    public bool RunsInside => Kind is BranchKind.InProcess or BranchKind.Command;

    [ObservableProperty]
    public partial HarnessMark Harness { get; private set; } = HarnessMark.None;

    public string KindText => BranchPhrases.Kind(Kind);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(Edge), nameof(IsWorking), nameof(IsWaiting), nameof(IsAlert), nameof(IsSettled))]
    public partial SubagentState State { get; private set; }

    public string StateText => SubagentStates.Text(State);

    public EdgeKind Edge => BranchPhrases.Edge(State);

    public bool IsWorking => State == SubagentState.Working;

    public bool IsWaiting => State == SubagentState.Waiting;

    public bool IsAlert => State is SubagentState.Failed or SubagentState.Lost;

    public bool IsSettled => State is SubagentState.Completed or SubagentState.Stopped;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBranches))]
    public partial int Branches { get; private set; }

    public bool HasBranches => Branches > 0;

    [ObservableProperty]
    public partial bool IsFolded { get; private set; }

    [RelayCommand]
    private void Fold() => fold(Key);

    IRelayCommand IBranchPlaceViewModel.FoldCommand => FoldCommand;

    public void Show(int depth, AgentBranch branch, bool folded)
    {
        Show(depth, branch.Kind, branch.State, branch.Children.Count, folded);
        Harness = branch.Harness;
    }

    public void Show(int depth, BranchKind kind, SubagentState state, int branches, bool folded)
    {
        Depth = depth;
        Kind = kind;
        State = state;
        Branches = branches;
        IsFolded = folded;
    }
}

internal static class BranchPhrases
{
    public static string Kind(BranchKind kind) => kind switch
    {
        BranchKind.Orchestrator => "orchestrator",
        BranchKind.InProcess => "sub-agent",
        BranchKind.Child => "child job",
        BranchKind.Command => "background command",
        _ => "cross-harness",
    };

    public static EdgeKind Edge(SubagentState state) => state switch
    {
        SubagentState.Working => EdgeKind.Flowing,
        SubagentState.Waiting => EdgeKind.Attention,
        SubagentState.Failed or SubagentState.Lost => EdgeKind.Failure,
        _ => EdgeKind.Quiet,
    };

    public static string Name(AgentBranch branch) => branch.Title;

    public static string Waits(AgentBranch branch) =>
        branch.Asks.Match(target => $"asks: {target}", () => branch.Node.Bind(node => node.Activity).Match(Sidebar.FactPhrases.Of, () => "waits for you"));

    public static string Doing(Fleet.DelegationTree tree)
    {
        var recent = AgentGlance.Recent(tree.RootJob);

        return recent.Count > 0 ? recent[0].Text : tree.RootJob.Match(job => Sidebar.FactPhrases.Of(job.Fact), () => Presenting.DelegationPhrases.Status(tree.Root.Status));
    }

    public static int AtWork(Fleet.DelegationTree tree) =>
        AgentBranches.Of(tree).Children.Count(child => child.Descendants.Prepend(child).Any(branch => branch.State is SubagentState.Working or SubagentState.Waiting));

    public static string Took(Jobs.Contracts.JobSummary job) =>
        job.Ended.Match(ended => Conversation.ConversationPhrases.Duration(ended - job.Submitted), () => string.Empty);

}
