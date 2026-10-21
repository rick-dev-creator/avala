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

internal interface IOrchestratorCardViewModel : IAgentBranch
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

    string Doing { get; }

    DateTimeOffset Since { get; }

    bool IsTiming { get; }

    string Took { get; }

    bool IsViewing { get; }

    IRelayCommand<JobId> OpenCommand { get; }

    ContextGlance Context { get; }

    string Tokens { get; }

}

[INotifyPropertyChanged]
internal sealed partial class OrchestratorCardViewModel : IOrchestratorCardViewModel
{
    private readonly StatusDotViewModel dot = new(default);
    private readonly BranchPlaceViewModel place;

    public OrchestratorCardViewModel(BranchPlaceViewModel place, DelegationTree tree)
    {
        this.place = place;
        Job = tree.Root.Job;
        Show(tree);
    }

    public IBranchPlaceViewModel Place => place;

    [ObservableProperty]
    public partial string Doing { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial DateTimeOffset Since { get; private set; }

    [ObservableProperty]
    public partial bool IsTiming { get; private set; }

    [ObservableProperty]
    public partial string Took { get; private set; } = string.Empty;

    public void Locate(int depth, AgentBranch branch, bool folded) => place.Show(depth, branch.Kind, branch.State, branch.Children.Count, folded);

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

    public void Show(DelegationTree tree)
    {
        var root = tree.Root;
        var children = tree.Children.Where(node => node.Depth == 1).ToList();
        Title = FactPhrases.Title(root.Instruction);
        Harness = tree.RootSpend.Session.Match(seen => seen.Provider.Name, () => string.Empty);
        Connection = root.Connection.Match(name => name.Value, () => tree.RootSpend.Session.Match(seen => seen.Connection.Value, () => "default connection"));
        var working = BranchPhrases.AtWork(tree);
        Detail = string.Join(
            " · ",
            new[] { OverviewPhrases.Repository(root.Repository), root.Autonomy.Match(autonomy => autonomy.ToString(), () => string.Empty), OverviewPhrases.Waiting(working) }
                .Where(part => part.Length > 0));
        Spent = $"{Amounts.Costs(Presenting.Shares.Total(tree.RootSpend.Spent.Concat(tree.Children.SelectMany(node => node.Spend.Spent))))} spent across the tree";
        var (hasBudget, budget, shares, shareNote) = tree.RootSpend.Caps.Match(caps => caps.CostPerJob, () => []) is [{ Amount: > 0 } cap, ..]
            ? Carved(cap, [.. children.Select(node => node.Spend.Carve.Match(carve => carve.Cost, () => []))])
            : (false, "No budget cap", [], string.Create(CultureInfo.InvariantCulture, $"{OverviewPhrases.Count(children.Count, "child job")}, no budget to share"));
        HasBudget = hasBudget;
        Budget = budget;
        Shares = Shares.SequenceEqual(shares) ? Shares : shares;
        ShareNote = shareNote;
        dot.Kind = tree.RootJob.Match(FactPhrases.Dot, () => OverviewPhrases.Dot(root.Status));
        Doing = BranchPhrases.Doing(tree);
        Since = root.Submitted;
        IsTiming = root.Ended.IsNone;
        Took = BranchPhrases.Took(root);
        Context = AgentGlance.Context(tree.RootJob);
        Tokens = AgentGlance.TokensText(AgentGlance.Tokens(tree.RootSpend) + tree.Children.Sum(node => AgentGlance.Tokens(node.Spend)));
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
