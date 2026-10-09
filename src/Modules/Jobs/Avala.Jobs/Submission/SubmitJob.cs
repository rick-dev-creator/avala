using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Ledger;
using Avala.Sdk;
using Avala.Sdk.Events;
using JobAnnouncement = Avala.Jobs.Contracts.JobSubmitted;

namespace Avala.Jobs.Submission;

internal sealed class SubmitJob(JobLedger ledger, IEventBus bus)
{
    public async Task<Result<JobId, JobError>> ExecuteAsync(
        string repository,
        string instruction,
        int attemptsPerRound,
        Option<Autonomy> autonomy,
        CancellationToken cancellationToken)
    {
        var submitted = RepositoryPath.Create(repository)
            .Bind(path => Instruction.Create(instruction)
                .Bind(text => AttemptBudget.Create(attemptsPerRound)
                    .Bind(budget => Job.Create(JobId.New(), text, budget, path, autonomy))))
            .Bind(job => job.Submit().Map(_ => job));

        if (!submitted.TryGetValue(out var job, out var error))
        {
            return error;
        }

        await ledger.RecordAsync(job, cancellationToken);
        await bus.PublishAsync(new JobAnnouncement(job.Id), cancellationToken);

        return job.Id;
    }
}
