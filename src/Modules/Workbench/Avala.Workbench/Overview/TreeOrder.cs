using Avala.Jobs.Contracts;
using Avala.Workbench.Fleet;

namespace Avala.Workbench.Overview;

internal static class TreeOrder
{
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
}
