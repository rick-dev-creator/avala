using Avala.Agents.Contracts.Events;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Spending;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Fleet;

internal sealed record DelegationNode(JobSummary Job, int Depth, Option<JobFact> Activity, Option<DelegationRecord> Delegation, JobSpend Spend)
{
    public Option<ChildReport> Report => Delegation.Bind(record => record.Report);

    public Option<BoardJob> Seen { get; init; }
}

internal sealed record DelegationTree(JobSummary Root, IReadOnlyList<DelegationNode> Children, IReadOnlyList<DelegationRecord> Refused)
{
    public JobSpend RootSpend { get; init; } = new(Option<Following.SessionSeen>.None, Option<Observability.Contracts.UsageSummary>.None, Option<Budgets.Contracts.BudgetCaps>.None, Option<Budgets.Contracts.BudgetCarve>.None);

    public Option<BoardJob> RootJob { get; init; }
}

internal sealed class DelegationReader(IJobCatalog catalog, IDelegations delegations, JobSpending spending, JobBoard board)
{
    public async ValueTask<IReadOnlyList<JobSummary>> OrchestratorsAsync(CancellationToken cancellationToken)
    {
        var jobs = await catalog.ListAsync(cancellationToken);
        var parents = jobs.Where(job => !job.Companion).Select(job => job.Parent)
            .Concat(delegations.All().Select(record => record.Parent))
            .SelectMany(parent => parent.Match<JobId[]>(found => [found], () => []))
            .ToHashSet();
        var archived = ArchivedJobs.On(board);

        return [.. jobs.Where(job => job.Parent.IsNone && parents.Contains(job.Job) && !archived.Hides(job.Job))];
    }

    public async ValueTask<Option<DelegationTree>> TreeAsync(JobId root, CancellationToken cancellationToken) =>
        (await catalog.TreeAsync(root, cancellationToken)).Map(tree => new DelegationTree(
            tree.Job,
            [.. tree.Children.Where(child => !child.Job.Companion).SelectMany(child => Nodes(child, 1))],
            [.. Refusals(tree)])
        {
            RootSpend = spending.Of(tree.Job.Job),
            RootJob = board.Find(tree.Job.Job),
        });

    public JobId RootOf(JobId job) =>
        board.Find(job).Bind(found => found.Summary.Parent).Match(parent => parent == job ? job : RootOf(parent), () => job);

    public bool InTree(JobId job) =>
        board.Find(job).Match(found => found.Summary.Parent.IsSome && !found.Summary.Companion, () => false)
        || board.Jobs.Values.Any(other => other.Summary.Parent == Option<JobId>.Some(job) && !other.Summary.Companion);

    public bool Offers(JobId job) => InTree(job) || Subagents(job).Any();

    public IReadOnlyList<string> Running(JobId job) => [.. Subagents(job).Where(tool => tool.Outcome.IsNone).Select(tool => tool.Key)];

    private IEnumerable<ToolEntry> Subagents(JobId job) =>
        board.Find(job).Match(found => found.Transcript.Entries.OfType<ToolEntry>().Where(tool => tool.Kind == ItemKind.Subagent), () => []);

    private IEnumerable<DelegationNode> Nodes(JobTree tree, int depth) =>
        tree.Children.Where(child => !child.Job.Companion).SelectMany(child => Nodes(child, depth + 1)).Prepend(new DelegationNode(
            tree.Job,
            depth,
            board.Find(tree.Job.Job).Map(job => job.Fact),
            delegations.OfChild(tree.Job.Job),
            spending.Of(tree.Job.Job)) { Seen = board.Find(tree.Job.Job) });

    private IEnumerable<DelegationRecord> Refusals(JobTree tree) =>
        delegations.OfParent(tree.Job.Job).Where(record => record.Refusal.IsSome)
            .Concat(tree.Children.SelectMany(Refusals));
}
