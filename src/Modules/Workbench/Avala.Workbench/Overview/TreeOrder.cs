using Avala.Components.Status;
using Avala.Jobs.Contracts;
using Avala.Workbench.Fleet;
using Avala.Workbench.Sidebar;

namespace Avala.Workbench.Overview;

internal sealed record ArrangedTree(IReadOnlyList<DelegationNode> Active, IReadOnlyList<DelegationNode> Settled);

internal static class TreeOrder
{
    public static ArrangedTree Arrange(IReadOnlyList<DelegationNode> nodes)
    {
        var branches = Branches(nodes).ToList();
        var settled = branches.Where(branch => branch.All(IsSettled)).ToList();

        return new ArrangedTree(
            [.. branches.Except(settled).OrderBy(branch => Rank(branch[0])).SelectMany(branch => branch)],
            [.. settled.SelectMany(branch => branch)]);
    }

    public static bool IsSettled(DelegationNode node) =>
        node.Report.IsSome || node.Job.Status is JobStatus.Approved or JobStatus.Discarded or JobStatus.Failed;

    public static void OneOpen(IEnumerable<DelegationNodeViewModel> rows, DelegationNodeViewModel opened)
    {
        if (!opened.IsOpen)
        {
            return;
        }

        foreach (var row in rows.Where(row => !ReferenceEquals(row, opened)))
        {
            row.IsOpen = false;
        }
    }

    private static int Rank(DelegationNode node) =>
        node.Seen.Match(FactPhrases.Dot, () => OverviewPhrases.Dot(node.Job.Status)) switch
        {
            StatusKind.NeedsYou or StatusKind.Held => 0,
            StatusKind.Working or StatusKind.Checking => 1,
            _ => 2,
        };

    private static IEnumerable<List<DelegationNode>> Branches(IReadOnlyList<DelegationNode> nodes)
    {
        var branch = new List<DelegationNode>();

        foreach (var node in nodes)
        {
            if (node.Depth == 1 && branch.Count > 0)
            {
                yield return branch;
                branch = [];
            }

            branch.Add(node);
        }

        if (branch.Count > 0)
        {
            yield return branch;
        }
    }
}
