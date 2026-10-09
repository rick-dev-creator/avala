using Avala.Autopilot.Contracts;
using Avala.Sdk;

namespace Avala.Autopilot.Sourcing;

internal sealed class FollowUpSource(ITaskLedger ledger) : IJobSource
{
    public const string Source = "follow-up";

    public string Name => Source;

    public async ValueTask<Result<SourceAnswer, AutopilotError>> NextAsync(SourceRequest request, CancellationToken cancellationToken) =>
        (await ledger.EntriesAsync(request.Repository, Source, cancellationToken))
            .Where(entry => entry.State == TaskState.Proposed)
            .OrderBy(entry => entry.At)
            .FirstOrDefault()
            .ToOption()
            .Match(entry => new SourceAnswer(entry.Task, Option<DateTimeOffset>.None), () => SourceAnswer.Nothing);

    public async ValueTask MarkAsync(SourcedTask task, TaskMark mark, CancellationToken cancellationToken) =>
        await ledger.MarkAsync(task, mark, cancellationToken);
}
