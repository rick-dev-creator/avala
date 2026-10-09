using Avala.Autopilot.Backlogs;
using Avala.Autopilot.Contracts;
using Avala.Sdk;

namespace Avala.Autopilot.Sourcing;

internal sealed class RecurringSource(IBacklogFile file, ITaskLedger ledger) : IJobSource
{
    public const string Source = "recurring";

    public string Name => Source;

    public async ValueTask<Result<SourceAnswer, AutopilotError>> NextAsync(SourceRequest request, CancellationToken cancellationToken)
    {
        if (!(await file.CurrentAsync(request.Repository, cancellationToken)).TryGetValue(out var backlog, out var error))
        {
            return error;
        }

        var taken = (await ledger.EntriesAsync(request.Repository, Source, cancellationToken))
            .SelectMany(entry => entry.Taken.Match<KeyValuePair<string, DateTimeOffset>[]>(at => [KeyValuePair.Create(entry.Task.Key, at)], () => []))
            .ToDictionary(StringComparer.Ordinal);
        var turn = backlog.Recurring.At(request.Now, taken);

        return new SourceAnswer(
            turn.Due.Map(task => new SourcedTask(Source, task.Key, request.Repository, task.Instruction)),
            turn.NextDue);
    }

    public async ValueTask MarkAsync(SourcedTask task, TaskMark mark, CancellationToken cancellationToken) =>
        await ledger.MarkAsync(task, mark, cancellationToken);
}
