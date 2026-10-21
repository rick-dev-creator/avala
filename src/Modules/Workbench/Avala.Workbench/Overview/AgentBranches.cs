using Avala.Agents.Contracts.Events;
using Avala.Components.Status;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Fleet;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Spending;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Overview;

internal enum BranchKind
{
    Orchestrator,
    InProcess,
    Child,
    CrossHarness,
}

internal sealed record AgentBranch(string Key, BranchKind Kind, SubagentState State, IReadOnlyList<AgentBranch> Children)
{
    public JobId Job { get; init; }

    public Option<DelegationNode> Node { get; init; }

    public Option<ToolEntry> Tool { get; init; }

    public Option<string> Asks { get; init; }

    public bool IsSettled => State is SubagentState.Completed or SubagentState.Stopped;

    public IEnumerable<AgentBranch> Descendants => Children.SelectMany(child => child.Descendants.Prepend(child));
}

internal static class AgentBranches
{
    public static AgentBranch Of(DelegationTree tree)
    {
        var byParent = tree.Children.ToLookup(node => node.Job.Parent);

        return new AgentBranch(
            tree.Root.Job.ToString(),
            BranchKind.Orchestrator,
            JobState(tree.Root, tree.RootJob),
            [.. Under(tree.Root.Job, tree.RootJob, Harness(tree.RootSpend), byParent)])
        {
            Job = tree.Root.Job,
        };
    }

    public static int Rank(SubagentState state) => state switch
    {
        SubagentState.Waiting => 0,
        SubagentState.Failed or SubagentState.Lost => 1,
        SubagentState.Working => 2,
        SubagentState.Completed => 3,
        _ => 4,
    };

    private static IEnumerable<AgentBranch> Under(JobId job, Option<BoardJob> seen, string harness, ILookup<Option<JobId>, DelegationNode> byParent) =>
        InProcess(job, seen)
            .Concat(byParent[Option<JobId>.Some(job)].Select(node => Child(node, harness, byParent)))
            .Select((branch, order) => (branch, order))
            .OrderBy(pair => Rank(pair.branch.State))
            .ThenBy(pair => pair.order)
            .Select(pair => pair.branch);

    private static AgentBranch Child(DelegationNode node, string parentHarness, ILookup<Option<JobId>, DelegationNode> byParent)
    {
        var harness = Harness(node.Spend);

        return new AgentBranch(
            node.Job.Job.ToString(),
            harness.Length > 0 && parentHarness.Length > 0 && !string.Equals(harness, parentHarness, StringComparison.Ordinal) ? BranchKind.CrossHarness : BranchKind.Child,
            JobState(node.Job, node.Seen),
            [.. Under(node.Job.Job, node.Seen, harness.Length > 0 ? harness : parentHarness, byParent)])
        {
            Job = node.Job.Job,
            Node = node,
        };
    }

    private static IEnumerable<AgentBranch> InProcess(JobId job, Option<BoardJob> seen) =>
        seen.Match<IEnumerable<AgentBranch>>(
            found =>
            {
                var asking = found.Transcript.Entries.OfType<PermissionEntry>().Where(permission => permission.AwaitsHuman).ToList();

                return found.Transcript.Entries.OfType<ToolEntry>()
                    .Where(tool => tool.Kind == ItemKind.Subagent)
                    .Select(tool =>
                    {
                        var asks = asking.Where(permission => tool.Children.Contains(permission.Item)).Select(permission => permission.Target).FirstOrDefault();

                        return new AgentBranch($"{job}/{tool.Key}", BranchKind.InProcess, ToolState(tool, asks is not null), [])
                        {
                            Job = job,
                            Tool = tool,
                            Asks = asks is null ? Option<string>.None : asks,
                        };
                    })
                    .ToList();
            },
            () => []);

    public static SubagentState ToolState(ToolEntry tool, bool asking) =>
        tool.Outcome.Match(
            outcome => outcome switch
            {
                ItemOutcome.Succeeded => SubagentState.Completed,
                ItemOutcome.Cancelled => SubagentState.Stopped,
                ItemOutcome.Abandoned => SubagentState.Lost,
                _ => SubagentState.Failed,
            },
            () => asking ? SubagentState.Waiting : SubagentState.Working);

    private static SubagentState JobState(JobSummary job, Option<BoardJob> seen) =>
        SubagentStates.Of(job.Status, job.Hold, seen.Match(found => FactPhrases.Dot(found) is StatusKind.NeedsYou && !OnlyItsSubagentsAsk(found), () => false));

    private static bool OnlyItsSubagentsAsk(BoardJob job)
    {
        var asking = job.Transcript.Entries.OfType<PermissionEntry>().Where(permission => permission.AwaitsHuman).Select(permission => permission.Item).ToList();
        var theirs = job.Transcript.Entries.OfType<ToolEntry>().Where(tool => tool.Kind == ItemKind.Subagent).SelectMany(tool => tool.Children).ToHashSet();

        return asking.Count > 0 && asking.All(theirs.Contains) && !job.Transcript.Entries.OfType<FormEntry>().Any(form => form.AwaitsHuman);
    }

    private static string Harness(JobSpend spend) => spend.Session.Match(session => session.Provider.Name, () => string.Empty);
}
