using System.Collections.Concurrent;
using Avala.Autopilot.Contracts;
using Avala.Autopilot.Sourcing;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Autopilot.Tests.Sourcing;

internal sealed class MemoryLedger : ITaskLedger
{
    private readonly ConcurrentDictionary<(string Repository, string Source, string Key), LedgerEntry> entries = new();

    public IReadOnlyList<LedgerEntry> All => [.. entries.Values];

    public Task<IReadOnlyList<LedgerEntry>> EntriesAsync(string repository, string source, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LedgerEntry>>([.. entries.Values.Where(entry => entry.Task.Repository == repository && entry.Task.Source == source)]);

    public Task ProposeAsync(SourcedTask task, DateTimeOffset at, CancellationToken cancellationToken)
    {
        entries[(task.Repository, task.Source, task.Key)] = new LedgerEntry(task, TaskState.Proposed, Option<JobId>.None, Option<DateTimeOffset>.None, at);

        return Task.CompletedTask;
    }

    public Task MarkAsync(SourcedTask task, TaskMark mark, CancellationToken cancellationToken)
    {
        var taken = mark.State == TaskState.Taken
            ? mark.At
            : entries.TryGetValue((task.Repository, task.Source, task.Key), out var known) ? known.Taken : Option<DateTimeOffset>.None;
        entries[(task.Repository, task.Source, task.Key)] = new LedgerEntry(task, mark.State, mark.Job, taken, mark.At);

        return Task.CompletedTask;
    }
}
