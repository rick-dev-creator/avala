using Avala.Autopilot.Backlogs;
using Avala.Autopilot.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Autopilot.Sourcing;

internal interface IBacklogFile
{
    Task<Result<BacklogDeclaration, AutopilotError>> CurrentAsync(string repository, CancellationToken cancellationToken);
}

internal sealed record LedgerEntry(SourcedTask Task, TaskState State, Option<JobId> Job, Option<DateTimeOffset> Taken, DateTimeOffset At);

internal interface ITaskLedger
{
    Task<IReadOnlyList<LedgerEntry>> EntriesAsync(string repository, string source, CancellationToken cancellationToken);

    Task ProposeAsync(SourcedTask task, DateTimeOffset at, CancellationToken cancellationToken);

    Task MarkAsync(SourcedTask task, TaskMark mark, CancellationToken cancellationToken);
}

internal static class RepositoryKey
{
    public static string Of(string repository) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(repository));
}
