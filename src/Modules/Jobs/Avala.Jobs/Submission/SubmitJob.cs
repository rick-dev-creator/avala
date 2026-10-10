using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Ledger;
using Avala.Sdk;
using Avala.Sdk.Events;
using JobAnnouncement = Avala.Jobs.Contracts.JobSubmitted;

namespace Avala.Jobs.Submission;

internal sealed class SubmitJob(JobLedger ledger, IEventBus bus, IConnections connections, TimeProvider clock)
{
    public async Task<Result<JobId, JobRejection>> ExecuteAsync(JobRequest request, CancellationToken cancellationToken)
    {
        var submitted = RepositoryPath.Create(request.RepositoryPath)
            .Bind(path => Instruction.Create(request.Instruction)
                .Bind(text => AttemptBudget.Create(request.AttemptsPerRound)
                    .Bind(budget => Job.Create(JobId.New(), text, budget, path, clock.GetUtcNow(), request.Autonomy, request.Connection, request.Parent, request.Model))))
            .Bind(job => job.Submit().Map(_ => job))
            .MapError(Rejection);

        if (!submitted.TryGetValue(out var job, out var rejection)
            || !(await UsableAsync(request.Connection, request.Model, cancellationToken)).TryGetValue(out _, out rejection)
            || !(await ParentOfAsync(job, cancellationToken)).TryGetValue(out _, out rejection))
        {
            return rejection;
        }

        await ledger.RecordAsync(job, cancellationToken);
        await request.Choice.Match(
            choice => job.Connection == Option<ConnectionName>.Some(choice.Connection) ? ledger.RecordChoiceAsync(job.Id, choice, cancellationToken) : Task.CompletedTask,
            () => Task.CompletedTask);
        await bus.PublishAsync(new JobAnnouncement(job.Id) { Parent = job.Parent }, cancellationToken);

        return job.Id;
    }

    private async Task<Result<bool, JobRejection>> UsableAsync(Option<ConnectionName> connection, ModelChoice model, CancellationToken cancellationToken) =>
        await connection.Match(
            async named => (await connections.CheckAsync(named, cancellationToken)).MapError(Refusal).Bind(found => Offered(found, model)),
            () => Task.FromResult(Result<bool, JobRejection>.Success(true)));

    private static Result<bool, JobRejection> Offered(ConnectionInfo connection, ModelChoice model) =>
        OffersModels.RefusalOn(connection.Capabilities, model).Match(
            refusal => Result<bool, JobRejection>.Failure(refusal == ModelRefusal.UnofferedModel ? JobRejection.UnofferedModel : JobRejection.UnofferedEffort),
            () => Result<bool, JobRejection>.Success(true));

    private async Task<Result<bool, JobRejection>> ParentOfAsync(Job child, CancellationToken cancellationToken) =>
        await child.Parent.Match(
            async parent => (await ledger.SnapshotAsync(parent, cancellationToken)).Match(
                found => found.State != JobState.Running ? JobRejection.ParentNotRunning
                    : found.Repository != child.Repository ? JobRejection.InvalidRequest
                    : Result<bool, JobRejection>.Success(true),
                () => JobRejection.UnknownParent),
            () => Task.FromResult(Result<bool, JobRejection>.Success(true)));

    private static JobRejection Refusal(ConnectionError error) => error switch
    {
        ConnectionError.UnknownConnection => JobRejection.UnknownConnection,
        ConnectionError.UnofferedModel => JobRejection.UnofferedModel,
        ConnectionError.UnofferedEffort => JobRejection.UnofferedEffort,
        _ => JobRejection.UnusableConnection,
    };

    private static JobRejection Rejection(JobError error) => error switch
    {
        JobError.EmptyRepository => JobRejection.EmptyRepository,
        JobError.EmptyInstruction => JobRejection.EmptyInstruction,
        JobError.InvalidAttemptBudget => JobRejection.InvalidAttemptBudget,
        _ => JobRejection.InvalidRequest,
    };
}
