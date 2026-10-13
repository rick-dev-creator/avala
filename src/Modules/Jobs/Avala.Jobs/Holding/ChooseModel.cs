using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Ledger;
using Avala.Sdk;

namespace Avala.Jobs.Holding;

internal sealed class ChooseModel(JobLedger ledger, IAgents agents, IConnections connections)
{
    public async Task<Result<JobModelChosen, JobRejection>> ExecuteAsync(Job job, ModelChoice model, CancellationToken cancellationToken)
    {
        if (job.State is not (JobState.NeedsHelp or JobState.AwaitingReview))
        {
            return JobRejection.NotHeld;
        }

        if (job.Model == model)
        {
            return new JobModelChosen(job.Id, model);
        }

        if (job.Resume.IsNone)
        {
            return JobRejection.NotResumable;
        }

        if (!(await OfferedAsync(job.Connection, model, cancellationToken)).TryGetValue(out _, out var rejection))
        {
            return rejection;
        }

        _ = job.ChooseModel(model);
        var chosen = new JobModelChosen(job.Id, model);
        await ledger.RecordModelAsync(job, chosen, cancellationToken);
        await job.Session.Match<Task>(
            session => agents.IsOpen(session) ? agents.StopAsync(session, cancellationToken).AsTask() as Task : Task.CompletedTask,
            () => Task.CompletedTask);

        return chosen;
    }

    private async Task<Result<bool, JobRejection>> OfferedAsync(Option<ConnectionName> connection, ModelChoice model, CancellationToken cancellationToken) =>
        (await connections.CheckAsync(connection, cancellationToken)).Match(
            found => OffersModels.RefusalOn(found.Capabilities, model).Match(
                refusal => Result<bool, JobRejection>.Failure(refusal == ModelRefusal.UnofferedModel ? JobRejection.UnofferedModel : JobRejection.UnofferedEffort),
                () => Result<bool, JobRejection>.Success(true)),
            _ => Result<bool, JobRejection>.Failure(JobRejection.UnusableConnection));
}

internal sealed record JobControls(HoldJob Hold, ChooseModel Model, JobJournal Journal);
