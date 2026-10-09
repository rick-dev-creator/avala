using Avala.Autopilot.Contracts;
using Avala.Sdk;

namespace Avala.Autopilot.Sourcing;

internal sealed class TaskSources(IEnumerable<IJobSource> sources)
{
    public async Task<Result<SourceAnswer, AutopilotError>> NextAsync(SourceRequest request, CancellationToken cancellationToken)
    {
        var nextDue = Option<DateTimeOffset>.None;

        foreach (var source in sources)
        {
            if (!(await source.NextAsync(request, cancellationToken)).TryGetValue(out var answer, out var error))
            {
                return error;
            }

            if (answer.Task.IsSome)
            {
                return answer;
            }

            nextDue = Earliest(nextDue, answer.NextDue);
        }

        return SourceAnswer.Nothing with { NextDue = nextDue };
    }

    public async Task MarkAsync(SourcedTask task, TaskMark mark, CancellationToken cancellationToken)
    {
        foreach (var source in sources.Where(source => source.Name == task.Source).Take(1))
        {
            await source.MarkAsync(task, mark, cancellationToken);
        }
    }

    private static Option<DateTimeOffset> Earliest(Option<DateTimeOffset> one, Option<DateTimeOffset> other) =>
        one.Match(
            first => other.Match(second => second < first ? second : first, () => first),
            () => other);
}
