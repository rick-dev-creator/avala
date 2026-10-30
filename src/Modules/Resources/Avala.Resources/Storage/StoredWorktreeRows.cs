using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;

namespace Avala.Resources.Storage;

internal sealed class StoredWorktreeEvent
{
    public int Key { get; init; }

    public Guid Job { get; init; }

    public string Path { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string Moment { get; init; } = string.Empty;

    public long Bytes { get; init; }

    public long At { get; init; }

    public long Lifetime { get; init; } = -1;

    public static StoredWorktreeEvent Of(WorktreeEvent recorded) => new()
    {
        Job = recorded.Job.Value,
        Path = recorded.Path,
        Status = recorded.Status.ToString(),
        Moment = recorded.Moment.Match(moment => moment.ToString(), () => string.Empty),
        Bytes = recorded.Bytes,
        At = recorded.At.UtcTicks,
        Lifetime = recorded.Lifetime.Match(lifetime => lifetime.Ticks, () => -1),
    };

    public WorktreeEvent Event() =>
        new(new JobId(Job), Path, Enum.Parse<JobStatus>(Status), Moment.Length == 0 ? Option<WorktreeMoment>.None : Enum.Parse<WorktreeMoment>(Moment), Bytes, new DateTimeOffset(At, TimeSpan.Zero))
        {
            Lifetime = Lifetime < 0 ? Option<TimeSpan>.None : TimeSpan.FromTicks(Lifetime),
        };
}

internal sealed class StoredWorktreeDay
{
    public string Day { get; init; } = string.Empty;

    public long Peak { get; set; }

    public int Worktrees { get; set; }

    public int Created { get; set; }

    public int Freed { get; set; }

    public long Recovered { get; set; }

    public static string Key(DateOnly day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public WorktreeDay Of() => new(DateOnly.ParseExact(Day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), Peak, Worktrees, Created, Freed, Recovered);
}

internal sealed class StoredSeenWorktree
{
    public string Path { get; init; } = string.Empty;

    public long First { get; init; }
}

internal sealed class StoredRestingJob
{
    public Guid Job { get; init; }

    public long Since { get; init; }
}
