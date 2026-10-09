using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Components.Status;
using Avala.Jobs.Contracts;
using Avala.Workbench.Fleet;
using Avala.Workbench.Presenting;
using Avala.Workbench.Sidebar;

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
}

internal sealed class OrchestratorCardViewModel : IOrchestratorCardViewModel
{
    public OrchestratorCardViewModel(DelegationTree tree)
    {
        var root = tree.Root;
        var children = tree.Children.Where(node => node.Depth == 1).ToList();
        var cap = tree.RootSpend.Caps.Match(caps => caps.CostPerJob, () => []);
        var carves = children.Select(node => node.Spend.Carve.Match(carve => carve.Cost, () => [])).ToList();
        Job = root.Job;
        Title = FactPhrases.Title(root.Instruction);
        Harness = tree.RootSpend.Session.Match(seen => seen.Provider.Name, () => string.Empty);
        Connection = root.Connection.Match(name => name.Value, () => tree.RootSpend.Session.Match(seen => seen.Connection.Value, () => "default connection"));
        var working = children.Count(node => node.Job.Status is JobStatus.Preparing or JobStatus.Running or JobStatus.Checking or JobStatus.NeedsHelp);
        Detail = string.Join(
            " · ",
            new[] { OverviewPhrases.Repository(root.Repository), root.Autonomy.Match(autonomy => autonomy.ToString(), () => string.Empty), OverviewPhrases.Waiting(working) }
                .Where(part => part.Length > 0));
        HasBudget = cap.Count > 0 && cap[0].Amount > 0;
        Budget = HasBudget ? $"Budget {Amounts.Costs([cap[0]])}" : "No budget cap";
        Spent = $"{Amounts.Costs(Presenting.Shares.Total(tree.RootSpend.Spent.Concat(tree.Children.SelectMany(node => node.Spend.Spent))))} spent across the tree";
        var carved = carves.Select(costs => HasBudget ? (double)Presenting.Shares.In(costs, cap[0].Currency) : 0).ToList();
        var kept = HasBudget ? Math.Max(0, (double)cap[0].Amount - carved.Sum()) : 0;
        Shares = HasBudget ? [new BudgetShare(kept, true), .. carved.Select(weight => new BudgetShare(weight, false))] : [];
        ShareNote = HasBudget
            ? string.Join(" · ", new[] { $"kept {Amounts.Costs([new Cost((decimal)kept, cap[0].Currency)])}" }.Concat(carves.Select((costs, index) => (index == 0 ? "carved " : string.Empty) + Amounts.Costs(costs))))
            : string.Create(CultureInfo.InvariantCulture, $"{OverviewPhrases.Count(children.Count, "sub-agent")}, no carve");
        Dot = new StatusDotViewModel(tree.RootJob.Match(FactPhrases.Dot, () => OverviewPhrases.Dot(root.Status)));
    }

    public JobId Job { get; }

    public string Title { get; }

    public string Harness { get; }

    public string Connection { get; }

    public string Detail { get; }

    public string Budget { get; }

    public string Spent { get; }

    public IReadOnlyList<BudgetShare> Shares { get; }

    public string ShareNote { get; }

    public bool HasBudget { get; }

    public IStatusDotViewModel Dot { get; }
}
