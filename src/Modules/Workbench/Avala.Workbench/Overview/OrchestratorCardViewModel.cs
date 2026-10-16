using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Components.Status;
using Avala.Jobs.Contracts;
using Avala.Workbench.Fleet;
using Avala.Workbench.Presenting;
using Avala.Workbench.Sidebar;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Overview;

internal interface IOrchestratorCardViewModel
{
    JobId Job { get; }

    string Title { get; }

    string Harness { get; }

    string Connection { get; }

    string Detail { get; }

    string Budget { get; }

    string Spent { get; }

    IReadOnlyList<BudgetShare> Shares { get; }

    string ShareNote { get; }

    bool HasBudget { get; }

    IStatusDotViewModel Dot { get; }

    IReadOnlyList<ISubagentViewModel> Subagents { get; }

    bool HasSubagents { get; }

    bool IsViewing { get; }

    IRelayCommand<JobId> OpenCommand { get; }

    ContextGlance Context { get; }

    string Tokens { get; }

    IReadOnlyList<TreeTally> Tally { get; }
}

internal sealed record TreeTally(string Text, StatusKind Kind);

[INotifyPropertyChanged]
internal sealed partial class OrchestratorCardViewModel : IOrchestratorCardViewModel
{
    private readonly StatusDotViewModel dot = new(default);

    public OrchestratorCardViewModel(DelegationTree tree)
    {
        Job = tree.Root.Job;
        Show(tree);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSubagents))]
    public partial IReadOnlyList<ISubagentViewModel> Subagents { get; private set; } = [];

    public bool HasSubagents => Subagents.Count > 0;

    [ObservableProperty]
    public partial bool IsViewing { get; set; }

    public IRelayCommand<JobId> OpenCommand { get; init; } = OverviewPhrases.Idle;

    public JobId Job { get; }

    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Harness { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Connection { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Detail { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Budget { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Spent { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<BudgetShare> Shares { get; private set; } = [];

    [ObservableProperty]
    public partial string ShareNote { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasBudget { get; private set; }

    public IStatusDotViewModel Dot => dot;

    [ObservableProperty]
    public partial ContextGlance Context { get; private set; } = ContextGlance.None;

    [ObservableProperty]
    public partial string Tokens { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<TreeTally> Tally { get; private set; } = [];

    public void Show(DelegationTree tree)
    {
        var root = tree.Root;
        var children = tree.Children.Where(node => node.Depth == 1).ToList();
        Title = FactPhrases.Title(root.Instruction);
        Harness = tree.RootSpend.Session.Match(seen => seen.Provider.Name, () => string.Empty);
        Connection = root.Connection.Match(name => name.Value, () => tree.RootSpend.Session.Match(seen => seen.Connection.Value, () => "default connection"));
        var working = children.Count(node => node.Job.Status is JobStatus.Preparing or JobStatus.Running or JobStatus.Checking or JobStatus.NeedsHelp);
        Detail = string.Join(
            " · ",
            new[] { OverviewPhrases.Repository(root.Repository), root.Autonomy.Match(autonomy => autonomy.ToString(), () => string.Empty), OverviewPhrases.Waiting(working) }
                .Where(part => part.Length > 0));
        Spent = $"{Amounts.Costs(Presenting.Shares.Total(tree.RootSpend.Spent.Concat(tree.Children.SelectMany(node => node.Spend.Spent))))} spent across the tree";
        var (hasBudget, budget, shares, shareNote) = tree.RootSpend.Caps.Match(caps => caps.CostPerJob, () => []) is [{ Amount: > 0 } cap, ..]
            ? Carved(cap, [.. children.Select(node => node.Spend.Carve.Match(carve => carve.Cost, () => []))])
            : (false, "No budget cap", [], string.Create(CultureInfo.InvariantCulture, $"{OverviewPhrases.Count(children.Count, "sub-agent")}, no budget to share"));
        HasBudget = hasBudget;
        Budget = budget;
        Shares = Shares.SequenceEqual(shares) ? Shares : shares;
        ShareNote = shareNote;
        dot.Kind = tree.RootJob.Match(FactPhrases.Dot, () => OverviewPhrases.Dot(root.Status));
        Subagents = SubagentViewModel.Kept(Subagents, SubagentViewModel.Of(tree.RootJob));
        Context = AgentGlance.Context(tree.RootJob);
        Tokens = AgentGlance.TokensText(AgentGlance.Tokens(tree.RootSpend) + tree.Children.Sum(node => AgentGlance.Tokens(node.Spend)));
        var tally = Tallied(tree);
        Tally = Tally.SequenceEqual(tally) ? Tally : tally;
    }

    private static IReadOnlyList<TreeTally> Tallied(DelegationTree tree)
    {
        var kinds = tree.Children.Select(node => node.Seen.Match(FactPhrases.Dot, () => OverviewPhrases.Dot(node.Job.Status))).ToList();
        var needing = kinds.Count(kind => kind is StatusKind.NeedsYou or StatusKind.Held);
        var running = kinds.Count(kind => kind is StatusKind.Working or StatusKind.Checking);
        var done = kinds.Count(kind => kind is StatusKind.Done or StatusKind.ReadyForReview or StatusKind.Failed);

        return
        [
            .. needing > 0 ? [new TreeTally($"{needing} needs you", StatusKind.NeedsYou)] : Array.Empty<TreeTally>(),
            .. running > 0 ? [new TreeTally($"{running} running", StatusKind.Working)] : Array.Empty<TreeTally>(),
            .. done > 0 ? [new TreeTally($"{done} done", StatusKind.Done)] : Array.Empty<TreeTally>(),
            .. tree.Refused.Count > 0 ? [new TreeTally($"{tree.Refused.Count} refused", StatusKind.Failed)] : Array.Empty<TreeTally>(),
        ];
    }

    private static (bool HasBudget, string Budget, IReadOnlyList<BudgetShare> Shares, string ShareNote) Carved(Cost cap, IReadOnlyList<IReadOnlyList<Cost>> carves)
    {
        var carved = carves.Select(costs => (double)Presenting.Shares.In(costs, cap.Currency)).ToList();
        var kept = Math.Max(0, (double)cap.Amount - carved.Sum());

        return (
            true,
            $"Budget {Amounts.Costs([cap])}",
            [new BudgetShare(kept, true), .. carved.Select(weight => new BudgetShare(weight, false))],
            string.Join(" · ", [$"kept {Amounts.Costs([new Cost((decimal)kept, cap.Currency)])}", .. carves.Select((costs, index) => (index == 0 ? "carved " : string.Empty) + Amounts.Costs(costs))]));
    }
}
