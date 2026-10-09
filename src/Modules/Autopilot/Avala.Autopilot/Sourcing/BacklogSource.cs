using Avala.Autopilot.Contracts;
using Avala.Sdk;

namespace Avala.Autopilot.Sourcing;

internal sealed class BacklogSource(IBacklogFile file, ITaskLedger ledger) : IJobSource
{
    public const string Source = "backlog";

    public string Name => Source;

    public async ValueTask<Result<SourceAnswer, AutopilotError>> NextAsync(SourceRequest request, CancellationToken cancellationToken)
    {
        if (!(await file.CurrentAsync(request.Repository, cancellationToken)).TryGetValue(out var backlog, out var error))
        {
            return error;
        }

        var handled = (await ledger.EntriesAsync(request.Repository, Source, cancellationToken)).Select(entry => entry.Task.Key).ToHashSet(StringComparer.Ordinal);

        return backlog.Tasks.FirstOrDefault(task => !handled.Contains(task.Key)).ToOption().Match(
            task => new SourceAnswer(new SourcedTask(Source, task.Key, request.Repository, task.Instruction), Option<DateTimeOffset>.None),
            () => SourceAnswer.Nothing);
    }

    public async ValueTask MarkAsync(SourcedTask task, TaskMark mark, CancellationToken cancellationToken) =>
        await ledger.MarkAsync(task, mark, cancellationToken);
}
