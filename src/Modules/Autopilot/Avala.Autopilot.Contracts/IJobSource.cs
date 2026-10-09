using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Autopilot.Contracts;

public interface IJobSource
{
    string Name { get; }

    ValueTask<Result<SourceAnswer, AutopilotError>> NextAsync(SourceRequest request, CancellationToken cancellationToken);

    ValueTask MarkAsync(SourcedTask task, TaskMark mark, CancellationToken cancellationToken);
}

public sealed record SourceRequest(LoopId Loop, string Repository, DateTimeOffset Now);

public sealed record SourcedTask(string Source, string Key, string Repository, string Instruction);

public sealed record SourceAnswer(Option<SourcedTask> Task, Option<DateTimeOffset> NextDue)
{
    public static SourceAnswer Nothing { get; } = new(Option<SourcedTask>.None, Option<DateTimeOffset>.None);
}

public enum TaskState
{
    Proposed,
    Taken,
    Approved,
    WaitingForPerson,
    Failed,
}

public sealed record TaskMark(TaskState State, Option<JobId> Job, DateTimeOffset At);
