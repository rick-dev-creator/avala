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

internal sealed record TreeClock(TimeProvider Time, Action Refresh);

internal sealed record ShownBranch(string Key, int Depth, AgentBranch Branch, bool Folded);

[INotifyPropertyChanged]
internal sealed partial class SubagentTreeViewModel(IRelayCommand<JobId> open, RowActions actions, TimeProvider clock, Action refresh) : ISubagentTreeViewModel, IDisposable
{
    public static readonly TimeSpan Linger = TimeSpan.FromSeconds(5);

    private readonly ObservableCollection<IAgentBranch> branches = [];
    private readonly HashSet<string> folded = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IAgentBranch> known = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> settledSince = new(StringComparer.Ordinal);
    private readonly HashSet<string> told = new(StringComparer.Ordinal);
    private ITimer? sweep;
    private bool disposed;
    private Option<DelegationTree> shown;
    private Option<JobId> viewing;

    public SubagentTreeViewModel(IRelayCommand<JobId> open, RowActions actions, Action refresh)
        : this(open, actions, TimeProvider.System, refresh)
    {
    }

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

    public void Dispose()
    {
        disposed = true;
        sweep?.Dispose();
        sweep = null;
    }

    public void Clear()
    {
        shown = Option<DelegationTree>.None;
        branches.Clear();
        Agents = Working = NeedsYou = Spent = string.Empty;
        Waiting = [];
    }

    private void Arrange()
    {
        foreach (var tree in shown.Match<DelegationTree[]>(found => [found], () => []))
        {
            var root = AgentBranches.Of(tree);
            var now = clock.GetUtcNow();
            Note(tree, root, now);
            branches.ShowInPlace(Visible(root, 0), (branch, next) => branch.Place.Key == next.Key, next => Known(tree, next), (branch, next) => Update(tree, branch, next));
            Forget(root);
            Summarize(tree, root);
        }
    }

    private IEnumerable<ShownBranch> Visible(AgentBranch branch, int depth)
    {
        var isFolded = folded.Contains(branch.Key);
        yield return new ShownBranch(branch.Key, depth, branch, isFolded);

        if (isFolded)
        {
            yield break;
        }

        foreach (var child in branch.Children.Where(child => !Gone(child)).SelectMany(child => Visible(child, depth + 1)))
        {
            yield return child;
        }
    }

    private bool Gone(AgentBranch branch) =>
        !(branch.Node.IsSome && viewing == Option<JobId>.Some(branch.Job))
        && settledSince.TryGetValue(branch.Key, out var since)
        && clock.GetUtcNow() - since >= Linger;

    private bool Leaves(DelegationTree tree, AgentBranch branch) =>
        branch.IsSettled || (branch.HasEnded && (branch.Kind is BranchKind.InProcess or BranchKind.Command || branch.Node.Bind(node => node.Report).IsSome || told.Contains(branch.Key) || !Reachable(tree, branch)));

    private void Note(DelegationTree tree, AgentBranch root, DateTimeOffset now)
    {
        var all = root.Descendants.ToList();
        told.RemoveWhere(key => !all.Any(branch => branch.Key == key && branch.State is SubagentState.Failed or SubagentState.Lost));

        foreach (var branch in all.Where(branch => !Leaves(tree, branch)))
        {
            settledSince.Remove(branch.Key);
        }

        foreach (var branch in all.Where(branch => Leaves(tree, branch)))
        {
            settledSince.TryAdd(branch.Key, now);
        }

        var present = all.Select(branch => branch.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var gone in settledSince.Keys.Where(key => !present.Contains(key)).ToList())
        {
            settledSince.Remove(gone);
        }

        var next = settledSince.Values.Select(since => since + Linger).Where(due => due > now).DefaultIfEmpty().Min();
        sweep?.Dispose();
        sweep = next == default || disposed ? null : clock.CreateTimer(_ => refresh(), null, next - now, Timeout.InfiniteTimeSpan);
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
        var present = root.Descendants.Prepend(root).Select(branch => branch.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var gone in known.Keys.Where(key => !present.Contains(key)).ToList())
        {
            known.Remove(gone);
        }
    }

    private IAgentBranch Create(DelegationTree tree, ShownBranch next)
    {
        var place = new BranchPlaceViewModel(next.Key, Fold, TellAsync);
        var created = next.Branch.Kind switch
        {
            BranchKind.Orchestrator => (IAgentBranch)new OrchestratorCardViewModel(place, tree) { OpenCommand = open },
            BranchKind.InProcess => new SubagentViewModel(place, next.Branch.Job) { OpenCommand = open },
            _ => next.Branch.Node.Match<IAgentBranch>(node => Node(place, node), () => new SubagentViewModel(place, next.Branch.Job) { OpenCommand = open }),
        };
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
        var branch = next.Branch;

        if (shownBranch.Place is BranchPlaceViewModel place)
        {
            place.Reach(Reachable(tree, branch));
        }

        switch (shownBranch)
        {
            case OrchestratorCardViewModel card:
                card.Show(tree);
                card.Locate(next.Depth, branch, next.Folded);
                card.IsViewing = viewing == Option<JobId>.Some(branch.Job);
                break;
            case DelegationNodeViewModel row:
                foreach (var node in branch.Node.Match<DelegationNode[]>(found => [found], () => []))
                {
                    row.Show(node);
                }

                row.Locate(next.Depth, branch, next.Folded);
                row.IsViewing = viewing == Option<JobId>.Some(branch.Job);
                actions.Peek(row);
                break;
            case SubagentViewModel agent:
                agent.Show(branch, next.Depth, next.Folded);
                break;
        }
    }

    private async Task<bool> TellAsync(string key)
    {
        var sent = await SendAsync(key).ConfigureAwait(true);

        if (sent && told.Add(key))
        {
            refresh();
        }

        return sent;
    }

    private Task<bool> SendAsync(string key)
    {
        var tree = shown.Match(found => found, () => throw new InvalidOperationException("No tree is shown."));
        var root = AgentBranches.Of(tree);
        var branch = root.Descendants.FirstOrDefault(found => found.Key == key);

        if (branch is null)
        {
            return Task.FromResult(false);
        }

        var (orchestrator, status) = Orchestrator(tree, branch);

        return actions.TellAsync(orchestrator, status, BranchPhrases.Told(branch), CancellationToken.None);
    }

    private static bool Reachable(DelegationTree tree, AgentBranch branch) =>
        Orchestrator(tree, branch).Status is not (JobStatus.Approved or JobStatus.Discarded or JobStatus.Failed);

    private static (JobId Job, JobStatus Status) Orchestrator(DelegationTree tree, AgentBranch branch)
    {
        var parent = branch.Node.Bind(node => node.Job.Parent).Match(found => found, () => branch.Job);
        var status = parent == tree.Root.Job ? tree.Root.Status : tree.Children.FirstOrDefault(node => node.Job.Job == parent)?.Job.Status ?? tree.Root.Status;

        return (parent, status);
    }

    private void Fold(string key)
    {
        if (!folded.Remove(key))
        {
            folded.Add(key);
        }

        Arrange();
    }

    private void Summarize(DelegationTree tree, AgentBranch root)
    {
        var agents = root.Descendants.Where(agent => !Gone(agent)).ToList();
        Agents = agents.Count == 0 && root.Descendants.Any() ? "no sub-agent at work" : agents.Count == 0 ? "no sub-agent yet" : OverviewPhrases.Count(agents.Count, "agent");
        var working = agents.Count(agent => agent.State == SubagentState.Working);
        Working = working == 0 ? string.Empty : $"{working} working";
        var waiting = agents.Prepend(root).Where(agent => agent.NeedsYou).ToList();
        NeedsYou = waiting.Count == 0 ? string.Empty : $"{waiting.Count} needs you";
        var spent = Shares.Total(tree.RootSpend.Spent.Concat(tree.Children.SelectMany(node => node.Spend.Spent)));
        Spent = spent.Count == 0 ? string.Empty : Amounts.Costs(spent);
        var listed = waiting.Select(agent => (IWaitingAgentViewModel)new WaitingAgentViewModel(agent.Job, BranchPhrases.Name(agent), BranchPhrases.Waits(agent), open)).ToList();
        Waiting = Waiting.SequenceEqual(listed) ? Waiting : listed;
    }
}
