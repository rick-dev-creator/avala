using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;

namespace Avala.Resources.History;

internal interface IWorktreeHistoryStore
{
    Task RecordAsync(WorktreeEvent recorded, CancellationToken cancellationToken);

    Task SaveDayAsync(WorktreeDay day, CancellationToken cancellationToken);

    Task<Option<WorktreeDay>> DayAsync(DateOnly day, CancellationToken cancellationToken);

    Task<bool> SeeAsync(string path, DateTimeOffset at, CancellationToken cancellationToken);

    Task<Option<DateTimeOffset>> FirstSeenAsync(string path, CancellationToken cancellationToken);

    Task<WorktreeReport> ReadAsync(DateOnly from, CancellationToken cancellationToken);

    Task RestAsync(JobId job, DateTimeOffset since, CancellationToken cancellationToken);

    Task WakeAsync(JobId job, CancellationToken cancellationToken);

    Task<Option<DateTimeOffset>> RestingSinceAsync(JobId job, CancellationToken cancellationToken);

    Task ForgetSeenAsync(string path, CancellationToken cancellationToken);
}
