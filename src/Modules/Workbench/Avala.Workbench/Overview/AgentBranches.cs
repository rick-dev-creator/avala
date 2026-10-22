using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
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
    Command,
}

internal sealed record AgentBranch(string Key, BranchKind Kind, SubagentState State, IReadOnlyList<AgentBranch> Children)
{
    public JobId Job { get; init; }

    public string Title { get; init; } = string.Empty;

    public Option<DelegationNode> Node { get; init; }

    public Option<ToolEntry> Tool { get; init; }

    public Option<string> Asks { get; init; }

    public HarnessMark Harness { get; init; } = HarnessMark.None;

    public bool GivenUp { get; init; }

    public bool WaitsForParent { get; init; }

    public bool NeedsYou => State == SubagentState.Waiting && !WaitsForParent;

    public bool IsSettled => State is SubagentState.Completed or SubagentState.Stopped && Children.All(child => child.IsSettled);

    public int Urgency => Children.Select(child => child.Urgency).Append(AgentBranches.Rank(State)).Min();

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
            [.. Under(tree.Root.Job, tree.RootJob, HarnessMark.Of(tree.RootSpend, tree.Root.Connection), byParent)])
        {
            Job = tree.Root.Job,
            Harness = HarnessMark.Of(tree.RootSpend, tree.Root.Connection),
            Title = FactPhrases.Title(tree.Root.Instruction),
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

    private static IEnumerable<AgentBranch> Under(JobId job, Option<BoardJob> seen, HarnessMark harness, ILookup<Option<JobId>, DelegationNode> byParent) =>
        InProcess(job, seen, harness)
            .Concat(byParent[Option<JobId>.Some(job)].Select(node => Child(node, harness, byParent)))
            .Select((branch, order) => (branch, order))
            .OrderBy(pair => pair.branch.Urgency)
            .ThenBy(pair => Rank(pair.branch.State))
            .ThenBy(pair => pair.order)
            .Select(pair => pair.branch);

    private static AgentBranch Child(DelegationNode node, HarnessMark parent, ILookup<Option<JobId>, DelegationNode> byParent)
    {
        var harness = HarnessMark.Of(node.Spend, node.Job.Connection);
        var running = harness.IsKnown ? harness : parent with { Instance = harness.HasInstance ? harness.Instance : parent.Instance };

        return new AgentBranch(
            node.Job.Job.ToString(),
            harness.IsKnown && parent.IsKnown && !string.Equals(harness.Name, parent.Name, StringComparison.Ordinal) ? BranchKind.CrossHarness : BranchKind.Child,
            JobState(node.Job, node.Seen),
            [.. Under(node.Job.Job, node.Seen, running, byParent)])
        {
            Job = node.Job.Job,
            Harness = running,
            Title = FactPhrases.Title(node.Job.Instruction),
            Node = node,
            GivenUp = node.Report.Match(report => report.Outcome == ChildOutcome.GivenUp, () => false),
            WaitsForParent = WaitsForParent(node.Seen),
        };
    }

    private static IEnumerable<AgentBranch> InProcess(JobId job, Option<BoardJob> seen, HarnessMark harness) =>
        seen.Match<IEnumerable<AgentBranch>>(
            found =>
            {
                var asking = Asking(found);

                return found.Transcript.Entries.OfType<ToolEntry>()
                    .Where(tool => tool.Kind == ItemKind.Subagent)
                    .Select(tool =>
                    {
                        var asks = asking.Where(ask => tool.Children.Contains(ask.Item)).Select(ask => ask.What).FirstOrDefault();

                        return new AgentBranch($"{job}/{tool.Key}", BranchKind.InProcess, ToolState(tool, asks is not null), [.. Commands(job, tool, harness)])
                        {
                            Job = job,
                            Harness = harness,
                            Title = tool.Named,
                            Tool = tool,
                            Asks = asks is null ? Option<string>.None : asks,
                        };
                    })
                    .ToList();
            },
            () => []);

    private static IEnumerable<AgentBranch> Commands(JobId job, ToolEntry subagent, HarnessMark harness) =>
        subagent.Background.Select(step =>
        {
            var entry = new ToolEntry($"{subagent.Key}/{step.Item.Value}", ItemKind.Command, step.Title, string.Empty, step.Outcome);

            return new AgentBranch($"{job}/{entry.Key}", BranchKind.Command, ToolState(entry, asking: false), [])
            {
                Job = job,
                Harness = harness,
                Title = step.Title,
                Tool = entry,
            };
        });

    private static List<(ItemId Item, string What)> Asking(BoardJob job) =>
    [
        .. job.Transcript.Entries.OfType<PermissionEntry>().Where(permission => permission.AwaitsHuman).Select(permission => (permission.Item, permission.Target)),
        .. job.Transcript.Entries.OfType<FormEntry>().Where(form => form.AwaitsHuman).Select(form => (form.Item, form.Form.Title)),
    ];

    public static SubagentState ToolState(ToolEntry tool, bool asking) =>
        tool.Outcome.Match(
            outcome => outcome switch
            {
                ItemOutcome.Succeeded => SubagentState.Completed,
                ItemOutcome.Cancelled or ItemOutcome.Expired => SubagentState.Stopped,
                ItemOutcome.Abandoned => SubagentState.Lost,
                _ => SubagentState.Failed,
            },
            () => asking ? SubagentState.Waiting : SubagentState.Working);

    private static SubagentState JobState(JobSummary job, Option<BoardJob> seen) =>
        SubagentStates.Of(job.Status, job.Hold, seen.Match(found => FactPhrases.Dot(found) is StatusKind.NeedsYou && !OnlyItsSubagentsAsk(found), () => false) || WaitsForParent(seen));

    public static bool WaitsForParent(Option<BoardJob> seen) => seen.Match(found => found.Fact.Kind == FactKind.WaitsForParent, () => false);

    private static bool OnlyItsSubagentsAsk(BoardJob job)
    {
        var asking = Asking(job);
        var theirs = job.Transcript.Entries.OfType<ToolEntry>().Where(tool => tool.Kind == ItemKind.Subagent).SelectMany(tool => tool.Children).ToHashSet();

        return asking.Count > 0 && asking.All(ask => theirs.Contains(ask.Item));
    }
}
