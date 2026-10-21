using System.Collections.ObjectModel;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Fleet;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Overview;

internal interface ISubagentTreeViewModel
{
    IReadOnlyList<IAgentBranch> Branches { get; }

    string Agents { get; }

    string Working { get; }

    string NeedsYou { get; }

    bool HasNeedsYou { get; }

    string Spent { get; }

    IReadOnlyList<IWaitingAgentViewModel> Waiting { get; }
}

internal interface IWaitingAgentViewModel
{
    JobId Job { get; }

    string Name { get; }

    string What { get; }

    IRelayCommand<JobId> OpenCommand { get; }
}

internal sealed record WaitingAgentViewModel(JobId Job, string Name, string What, IRelayCommand<JobId> OpenCommand) : IWaitingAgentViewModel;

internal sealed record ShownBranch(string Key, int Depth, Option<AgentBranch> Branch, IReadOnlyList<AgentBranch> Settled, bool Folded);

[INotifyPropertyChanged]
internal sealed partial class SubagentTreeViewModel(IRelayCommand<JobId> open, RowActions actions) : ISubagentTreeViewModel
{
    public const int FoldsAfter = 3;

    private const string SettledKey = "/settled";

    private readonly ObservableCollection<IAgentBranch> branches = [];
    private readonly HashSet<string> folded = new(StringComparer.Ordinal);
    private readonly HashSet<string> opened = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IAgentBranch> known = new(StringComparer.Ordinal);
    private Option<DelegationTree> shown;
    private Option<JobId> viewing;

    public IReadOnlyList<IAgentBranch> Branches => branches;

    [ObservableProperty]
    public partial string Agents { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Working { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNeedsYou))]
    public partial string NeedsYou { get; private set; } = string.Empty;

    public bool HasNeedsYou => NeedsYou.Length > 0;

    [ObservableProperty]
    public partial string Spent { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<IWaitingAgentViewModel> Waiting { get; private set; } = [];

    public Option<OrchestratorCardViewModel> Root => branches.OfType<OrchestratorCardViewModel>().FirstOrDefault() is { } root ? root : Option<OrchestratorCardViewModel>.None;

    public IEnumerable<DelegationNodeViewModel> Jobs => branches.OfType<DelegationNodeViewModel>();

    public void Show(DelegationTree tree, Option<JobId> viewed)
    {
        shown = tree;
        viewing = viewed;
        Arrange();
    }

    public void Clear()
    {
        shown = Option<DelegationTree>.None;
        branches.Clear();
        known.Clear();
        Agents = Working = NeedsYou = Spent = string.Empty;
        Waiting = [];
    }

    private void Arrange()
    {
        foreach (var tree in shown.Match<DelegationTree[]>(found => [found], () => []))
        {
            var root = AgentBranches.Of(tree);
            branches.ShowInPlace(Visible(root, 0), (branch, next) => branch.Place.Key == next.Key, next => Known(tree, next), (branch, next) => Update(tree, branch, next));
            Forget(root);
            Summarize(tree, root);
        }
    }

    private IEnumerable<ShownBranch> Visible(AgentBranch branch, int depth)
    {
        var isFolded = folded.Contains(branch.Key);
        yield return new ShownBranch(branch.Key, depth, branch, [], isFolded);

        if (isFolded)
        {
            yield break;
        }

        var settled = branch.Children.Where(child => child.IsSettled).ToList();
        var folds = settled.Count > FoldsAfter;

        foreach (var child in branch.Children.Where(child => !folds || !child.IsSettled).SelectMany(child => Visible(child, depth + 1)))
        {
            yield return child;
        }

        if (!folds)
        {
            yield break;
        }

        var key = branch.Key + SettledKey;
        var isOpen = opened.Contains(key);
        yield return new ShownBranch(key, depth + 1, Option<AgentBranch>.None, settled, !isOpen);

        foreach (var child in isOpen ? settled.SelectMany(child => Visible(child, depth + 1)) : [])
        {
            yield return child;
        }
    }

    private IAgentBranch Known(DelegationTree tree, ShownBranch next)
    {
        if (known.TryGetValue(next.Key, out var kept))
        {
            Update(tree, kept, next);

            return kept;
        }

        var created = Create(tree, next);
        known[next.Key] = created;

        return created;
    }

    private void Forget(AgentBranch root)
    {
        var present = root.Descendants.Prepend(root).SelectMany(branch => new[] { branch.Key, branch.Key + SettledKey }).ToHashSet(StringComparer.Ordinal);

        foreach (var gone in known.Keys.Where(key => !present.Contains(key)).ToList())
        {
            known.Remove(gone);
        }
    }

    private IAgentBranch Create(DelegationTree tree, ShownBranch next)
    {
        var place = new BranchPlaceViewModel(next.Key, next.Branch.IsSome ? Fold : OpenSettled);
        var created = next.Branch.Match<IAgentBranch>(
            branch => branch.Kind switch
            {
                BranchKind.Orchestrator => new OrchestratorCardViewModel(place, tree) { OpenCommand = open },
                BranchKind.InProcess => new SubagentViewModel(place, branch.Job) { OpenCommand = open },
                _ => branch.Node.Match<IAgentBranch>(node => Node(place, node), () => new SubagentViewModel(place, branch.Job) { OpenCommand = open }),
            },
            () => new FoldedBranchesViewModel(place));
        Update(tree, created, next);

        return created;
    }

    private DelegationNodeViewModel Node(BranchPlaceViewModel place, DelegationNode node)
    {
        var row = new DelegationNodeViewModel(place, node) { OpenCommand = open };
        row.Toggled += (_, _) => TreeOrder.OneOpen(Jobs, row);
        row.Peeked += (_, _) => actions.Peek(row);

        return row;
    }

    private void Update(DelegationTree tree, IAgentBranch shownBranch, ShownBranch next)
    {
        switch (shownBranch, next.Branch.Match<AgentBranch?>(found => found, () => null))
        {
            case (FoldedBranchesViewModel stub, _):
                stub.Show(next.Depth, next.Settled, next.Folded);
                break;
            case (OrchestratorCardViewModel card, { } branch):
                card.Show(tree);
                card.Locate(next.Depth, branch, next.Folded);
                card.IsViewing = viewing == Option<JobId>.Some(branch.Job);
                break;
            case (DelegationNodeViewModel row, { } branch):
                foreach (var node in branch.Node.Match<DelegationNode[]>(found => [found], () => []))
                {
                    row.Show(node);
                }

                row.Locate(next.Depth, branch, next.Folded);
                row.IsViewing = viewing == Option<JobId>.Some(branch.Job);
                actions.Peek(row);
                break;
            case (SubagentViewModel agent, { } branch):
                agent.Show(branch, next.Depth, next.Folded);
                break;
        }
    }

    private void Fold(string key)
    {
        if (!folded.Remove(key))
        {
            folded.Add(key);
        }

        Arrange();
    }

    private void OpenSettled(string key)
    {
        if (!opened.Remove(key))
        {
            opened.Add(key);
        }

        Arrange();
    }

    private void Summarize(DelegationTree tree, AgentBranch root)
    {
        var agents = root.Descendants.ToList();
        Agents = agents.Count == 0 ? "no sub-agent yet" : OverviewPhrases.Count(agents.Count, "agent");
        var working = agents.Count(agent => agent.State == SubagentState.Working);
        Working = working == 0 ? string.Empty : $"{working} working";
        var waiting = agents.Where(agent => agent.State == SubagentState.Waiting).ToList();
        NeedsYou = waiting.Count == 0 ? string.Empty : $"{waiting.Count} needs you";
        var spent = Shares.Total(tree.RootSpend.Spent.Concat(tree.Children.SelectMany(node => node.Spend.Spent)));
        Spent = spent.Count == 0 ? string.Empty : Amounts.Costs(spent);
        var listed = waiting.Select(agent => (IWaitingAgentViewModel)new WaitingAgentViewModel(agent.Job, BranchPhrases.Name(agent), BranchPhrases.Waits(agent), open)).ToList();
        Waiting = Waiting.SequenceEqual(listed) ? Waiting : listed;
    }
}
