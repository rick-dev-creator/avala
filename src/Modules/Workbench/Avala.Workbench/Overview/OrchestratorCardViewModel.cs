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
        Job = root.Job;
        Title = FactPhrases.Title(root.Instruction);
        Harness = tree.RootSpend.Session.Match(seen => seen.Provider.Name, () => string.Empty);
        Connection = root.Connection.Match(name => name.Value, () => tree.RootSpend.Session.Match(seen => seen.Connection.Value, () => "default connection"));
        var working = children.Count(node => node.Job.Status is JobStatus.Preparing or JobStatus.Running or JobStatus.Checking or JobStatus.NeedsHelp);
        Detail = string.Join(
            " · ",
            new[] { OverviewPhrases.Repository(root.Repository), root.Autonomy.Match(autonomy => autonomy.ToString(), () => string.Empty), OverviewPhrases.Waiting(working) }
                .Where(part => part.Length > 0));
        Spent = $"{Amounts.Costs(Presenting.Shares.Total(tree.RootSpend.Spent.Concat(tree.Children.SelectMany(node => node.Spend.Spent))))} spent across the tree";
        (HasBudget, Budget, Shares, ShareNote) = tree.RootSpend.Caps.Match(caps => caps.CostPerJob, () => []) is [{ Amount: > 0 } cap, ..]
            ? Carved(cap, [.. children.Select(node => node.Spend.Carve.Match(carve => carve.Cost, () => []))])
            : (false, "No budget cap", [], string.Create(CultureInfo.InvariantCulture, $"{OverviewPhrases.Count(children.Count, "sub-agent")}, no carve"));
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
