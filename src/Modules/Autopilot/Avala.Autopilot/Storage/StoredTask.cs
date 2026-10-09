using Avala.Autopilot.Contracts;
using Avala.Autopilot.Sourcing;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Autopilot.Storage;

internal sealed class StoredTask
{
    public int Key { get; init; }

    public string Repository { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public string TaskKey { get; init; } = string.Empty;

    public string Instruction { get; init; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public Guid Job { get; set; }

    public long Taken { get; set; } = -1;

    public long At { get; set; }

    public static StoredTask Of(SourcedTask task, DateTimeOffset at) => new()
    {
        Repository = task.Repository,
        Source = task.Source,
        TaskKey = task.Key,
        Instruction = task.Instruction,
        State = TaskState.Proposed.ToString(),
        At = at.UtcTicks,
    };

    public void Mark(TaskMark mark)
    {
        State = mark.State.ToString();
        Job = mark.Job.Match(job => job.Value, () => Guid.Empty);
        Taken = mark.State == TaskState.Taken ? mark.At.UtcTicks : Taken;
        At = mark.At.UtcTicks;
    }

    public LedgerEntry Entry() => new(
        new SourcedTask(Source, TaskKey, Repository, Instruction),
        Enum.Parse<TaskState>(State),
        Job == Guid.Empty ? Option<JobId>.None : new JobId(Job),
        Taken < 0 ? Option<DateTimeOffset>.None : new DateTimeOffset(Taken, TimeSpan.Zero),
        new DateTimeOffset(At, TimeSpan.Zero));
}
