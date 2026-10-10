using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Permissions.Tests;

internal sealed class WorkingFiles : IWorkingFiles
{
    public Option<string> Content { get; set; }

    public string WrittenIn { get; private set; } = string.Empty;

    public ValueTask<Result<Option<string>, WorkspaceFailure>> ReadAsync(string repository, string path, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<Option<string>, WorkspaceFailure>.Success(Content));

    public ValueTask<Result<string, WorkspaceFailure>> WriteAsync(string repository, string path, string content, CancellationToken cancellationToken)
    {
        Assert.Equal(".avala/permissions.json", path);
        (WrittenIn, Content) = (repository, content);

        return ValueTask.FromResult(Result<string, WorkspaceFailure>.Success(path));
    }
}

internal sealed class OneJobCatalog(JobId known, string repository) : IJobCatalog
{
    public ValueTask<Option<JobHistory>> HistoryAsync(JobId job, CancellationToken cancellationToken) =>
        ValueTask.FromResult(job == known
            ? Option<JobHistory>.Some(new JobHistory(
                new JobSummary(job, repository, "Migrate", DateTimeOffset.UnixEpoch, JobStatus.Running, Option<ConnectionName>.None, Option<Autonomy>.None, Option<WorkspaceId>.None),
                [],
                []))
            : Option<JobHistory>.None);

    public ValueTask<IReadOnlyList<JobSummary>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<IReadOnlyList<JobSummary>> ChildrenAsync(JobId parent, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Option<JobTree>> TreeAsync(JobId root, CancellationToken cancellationToken) => throw new NotSupportedException();
}
