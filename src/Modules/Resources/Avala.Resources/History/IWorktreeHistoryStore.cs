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
}
