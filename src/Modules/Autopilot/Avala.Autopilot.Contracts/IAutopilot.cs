using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Autopilot.Contracts;

public interface IAutopilot
{
    ValueTask<Result<LoopId, AutopilotError>> StartAsync(LoopRequest request, CancellationToken cancellationToken);

    ValueTask<Result<LoopId, AutopilotError>> PauseAsync(LoopId loop, CancellationToken cancellationToken);

    ValueTask<Result<LoopId, AutopilotError>> ResumeAsync(LoopId loop, CancellationToken cancellationToken);

    ValueTask<Result<LoopId, AutopilotError>> StopAsync(LoopId loop, CancellationToken cancellationToken);

    IReadOnlyList<LoopState> Loops();

    Option<LoopDigest> DigestOf(LoopId loop);
}

public readonly record struct LoopId(Guid Value)
{
    public static LoopId New() => new(Guid.CreateVersion7());
}

public sealed record LoopRequest(string Repository)
{
    public Option<ConnectionName> Connection { get; init; }

    public Option<Autonomy> Autonomy { get; init; }

    public int AttemptsPerRound { get; init; } = 3;

    public LoopLimits Limits { get; init; } = new();
}

public sealed record LoopLimits
{
    public IReadOnlyList<Cost> SpendPerLoop { get; init; } = [];

    public IReadOnlyList<Cost> SpendPerWindow { get; init; } = [];

    public TimeSpan Window { get; init; } = TimeSpan.FromDays(1);

    public int FailuresInARow { get; init; } = 3;

    public int SameFailure { get; init; } = 2;

    public int NothingChanged { get; init; } = 2;

    public int Iterations { get; init; } = 50;

    public double PauseAtLimit { get; init; } = 0.9;
}

public enum LoopStatus
{
    Running,
    Waiting,
    Paused,
    Ended,
}

public enum PauseReason
{
    Command,
    UsageLimit,
}

public enum LoopEnding
{
    Drained,
    Stopped,
    BreakerTripped,
    SourceFailed,
    Failed,
}

public sealed record LoopState(LoopId Loop, string Repository, LoopStatus Status, DateTimeOffset Started, int Iterations, Option<JobId> Current)
{
    public Option<DateTimeOffset> Until { get; init; }

    public Option<PauseReason> Pause { get; init; }

    public Option<LoopEnding> Ending { get; init; }

    public Option<Breaker> Breaker { get; init; }

    public Option<AutopilotError> Error { get; init; }

    public Option<string> Fault { get; init; }
}

public enum AutopilotError
{
    EmptyRepository,
    InvalidLimits,
    AlreadyRunning,
    UnknownLoop,
    NotRunning,
    NotPaused,
    LoopEnded,
    Unreadable,
    TooLarge,
    Malformed,
    UnknownField,
    InvalidKey,
    DuplicateKey,
    MissingInstruction,
    InvalidInterval,
    UnknownRule,
}
