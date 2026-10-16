using System.Globalization;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Ledger;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Jobs.Review;

internal sealed class JobCheckpointDesk(JobQueues queues, IWorkspaceCheckpoints checkpoints) : IJobCheckpoints
{
    public const string AttemptLabel = "Attempt ";

    public async ValueTask<IReadOnlyList<JobCheckpoint>> OfAsync(JobId job, CancellationToken cancellationToken) =>
        (await queues.RunAsync(job, (found, token) => ListedAsync(found, token), cancellationToken)).Match(listed => listed, () => []);

    public async ValueTask<Result<CheckpointRestored, JobRejection>> RestoreAsync(JobId job, int attempt, CancellationToken cancellationToken) =>
        (await queues.RunAsync(job, (found, token) => RestoredAsync(found, attempt, token), cancellationToken))
            .Match(done => done, () => Result<CheckpointRestored, JobRejection>.Failure(JobRejection.UnknownJob));

    private async Task<IReadOnlyList<JobCheckpoint>> ListedAsync(Job job, CancellationToken cancellationToken) =>
        await job.Workspace.Match(
            async workspace => (await checkpoints.ListAsync(workspace, cancellationToken)).Match(Attempts, _ => []),
            () => Task.FromResult<IReadOnlyList<JobCheckpoint>>([]));

    private async Task<Result<CheckpointRestored, JobRejection>> RestoredAsync(Job job, int attempt, CancellationToken cancellationToken)
    {
        if (job.State is not (JobState.NeedsHelp or JobState.AwaitingReview))
        {
            return JobRejection.CannotRestore;
        }

        if (!job.Workspace.ToResult(JobRejection.WorkspaceUnavailable).TryGetValue(out var workspace, out var rejection))
        {
            return rejection;
        }

        var target = (await ListedAsync(job, cancellationToken)).LastOrDefault(checkpoint => checkpoint.Attempt == attempt);

        if (target is null)
        {
            return JobRejection.UnknownCheckpoint;
        }

        return (await checkpoints.RestoreAsync(workspace, target.Number, cancellationToken)).Match(
            restored => Result<CheckpointRestored, JobRejection>.Success(new CheckpointRestored(job.Id, attempt, restored.Files)),
            _ => Result<CheckpointRestored, JobRejection>.Failure(JobRejection.WorkspaceUnavailable));
    }

    private static IReadOnlyList<JobCheckpoint> Attempts(IReadOnlyList<CheckpointChanges> listed) =>
        [.. listed.SelectMany(checkpoint => checkpoint.Label.StartsWith(AttemptLabel, StringComparison.Ordinal)
            && int.TryParse(checkpoint.Label[AttemptLabel.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var attempt)
                ? [new JobCheckpoint(attempt, checkpoint.Number, checkpoint.Files)]
                : Array.Empty<JobCheckpoint>())];
}
