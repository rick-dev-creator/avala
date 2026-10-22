using Avala.Delegation.Contracts;
using System.Collections.ObjectModel;
using Avala.Components.Status;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Fleet;
using Avala.Workbench.Presenting;
using Avala.Workbench.Sidebar;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Overview;

internal interface IOrchestrationViewModel
{
    JobId Job { get; }

    string Title { get; }

    string Repository { get; }

    string Size { get; }

    string NeedsYou { get; }

    string Tokens { get; }

    string Spent { get; }

    bool IsActive { get; }

    bool IsCollapsed { get; }

    IRelayCommand ToggleCommand { get; }

    ISubagentTreeViewModel Tree { get; }

    IReadOnlyList<IDelegationRefusalViewModel> Refused { get; }
}

[INotifyPropertyChanged]
internal sealed partial class OrchestrationViewModel : IOrchestrationViewModel, IDisposable
{
    private readonly ObservableCollection<DelegationRefusalViewModel> refused = [];
    private readonly SubagentTreeViewModel tree;
    private bool chosen;

    public OrchestrationViewModel(DelegationTree tree, IRelayCommand<JobId> open, RowActions actions)
        : this(tree, open, actions, new TreeClock(TimeProvider.System, () => { }))
    {
    }

    public OrchestrationViewModel(DelegationTree tree, IRelayCommand<JobId> open, RowActions actions, TreeClock clock)
    {
        Job = tree.Root.Job;
        this.tree = new SubagentTreeViewModel(open, actions, clock.Time, clock.Refresh);
        Show(tree);
        IsCollapsed = !IsActive;
    }

    public JobId Job { get; }

    public void Dispose() => tree.Dispose();

    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Repository { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Size { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string NeedsYou { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Tokens { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Spent { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsActive { get; private set; }

    [ObservableProperty]
    public partial bool IsCollapsed { get; private set; }

    public DateTimeOffset LastActive { get; private set; }

    public int AtWork { get; private set; }

    public int Needing { get; private set; }

    public IReadOnlyList<string> Connections { get; private set; } = [];

    public ISubagentTreeViewModel Tree => tree;

    public SubagentTreeViewModel Shown => tree;

    public IReadOnlyList<IDelegationRefusalViewModel> Refused => refused;

    public void Show(DelegationTree tree)
    {
        this.tree.Show(tree, Option<JobId>.None);
        Title = FactPhrases.Title(tree.Root.Instruction);
        Repository = OverviewPhrases.Repository(tree.Root.Repository);
        var rooted = AgentBranches.Of(tree);
        var branches = rooted.Descendants.ToList();
        var agents = branches.Count(branch => branch.Kind == BranchKind.InProcess);
        AtWork = branches.Count(branch => !branch.IsSettled);
        Needing = branches.Prepend(rooted).Count(branch => branch.NeedsYou);
        Size = agents == 0
            ? OverviewPhrases.Count(tree.Children.Count + 1, "job")
            : $"{OverviewPhrases.Count(tree.Children.Count + 1, "job")} · {OverviewPhrases.Count(agents, "agent")}";
        NeedsYou = Needing == 0 ? string.Empty : $"{Needing} needs you";
        Tokens = AgentGlance.TokensText(AgentGlance.Tokens(tree.RootSpend) + tree.Children.Sum(node => AgentGlance.Tokens(node.Spend)));
        var spent = Presenting.Shares.Total(tree.RootSpend.Spent.Concat(tree.Children.SelectMany(node => node.Spend.Spent)));
        Spent = spent.Count == 0 ? string.Empty : Amounts.Costs(spent);
        IsActive = Working(tree.Root.Status) || tree.Children.Any(node => Working(node.Job.Status));
        IsCollapsed = chosen ? IsCollapsed : !IsActive;
        LastActive = Active(tree);
        Connections = [.. tree.Children.Select(node => node.Job.Connection).Prepend(tree.Root.Connection).SelectMany(name => name.Match<string[]>(found => [found.Value], () => [])).Distinct(StringComparer.Ordinal)];
        refused.ShowOnly(tree.Refused.Select(record => new DelegationRefusalViewModel(record)));
    }

    [RelayCommand]
    private void Toggle()
    {
        chosen = true;
        IsCollapsed = !IsCollapsed;
    }

    private static bool Working(JobStatus status) => status is JobStatus.Preparing or JobStatus.Running or JobStatus.Checking or JobStatus.NeedsHelp;

    private static DateTimeOffset Active(DelegationTree tree) =>
        tree.Children.Select(node => (node.Seen, node.Job.Submitted))
            .Prepend((tree.RootJob, tree.Root.Submitted))
            .Select(job => job.Item1.Match(
                found => new[] { found.Transcript.LiveSince, found.Transcript.QuietSince }.SelectMany(at => at.Match<DateTimeOffset[]>(time => [time], () => [])).DefaultIfEmpty(job.Submitted).Max(),
                () => job.Submitted))
            .Max();
}
