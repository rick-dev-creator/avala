using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Triggers.Contracts;
using Avala.Triggers.Declarations;
using Avala.Triggers.Looping;

namespace Avala.Triggers.Firing;

internal sealed class FiringChecks(IJobCatalog catalog, IEnumerable<IRepositoryPolicies> policies, RunJournal journal, LoopQueue queue)
{
    private static readonly JobStatus[] Unsettled = [JobStatus.Draft, JobStatus.Preparing, JobStatus.Running, JobStatus.Checking, JobStatus.NeedsHelp];

    public async Task<int> RunningAsync(TriggerId trigger, CancellationToken cancellationToken)
    {
        var jobs = journal.JobsOf(trigger).ToHashSet();

        if (jobs.Count == 0)
        {
            return queue.CountOf(trigger);
        }

        var listed = await catalog.ListAsync(cancellationToken);

        return listed.Count(job => jobs.Contains(job.Job) && Unsettled.Contains(job.Status)) + queue.CountOf(trigger);
    }

    public async Task<Autonomy> AutonomyAsync(TriggerDeclaration trigger, CancellationToken cancellationToken)
    {
        var declared = Autonomy.Supervised;

        foreach (var policy in policies.Take(1))
        {
            var repository = await policy.OfRepositoryAsync(trigger.Repository, cancellationToken);
            declared = repository.File == PolicyFileStatus.Applied ? repository.Autonomy : Autonomy.Supervised;
        }

        return trigger.Autonomy < declared ? trigger.Autonomy : declared;
    }
}
