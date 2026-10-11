using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Triggers.Contracts;

namespace Avala.Forges.Contracts;

public interface IForgeEvents
{
    ValueTask<Result<ForgeEventPage, ForgeError>> ReadSinceAsync(ForgeContext context, ForgeEventQuery query, CancellationToken cancellationToken);

    HookSignature Signature { get; }
}

public sealed record HookSignature(IReadOnlyList<string> Headers, string Prefix);

public sealed record ForgeEventQuery(ForgeEventKind Kind, Option<string> Cursor, DateTimeOffset Now)
{
    public const int DefaultMost = 20;

    public int Most { get; init; } = DefaultMost;

    public Option<string> Branch { get; init; }
}

public sealed record ForgeEventPage(IReadOnlyList<ForgeEvent> Events, string Cursor);

public sealed record ForgeEvent(ForgeEventKind Kind, string Id, string Cursor, string Actor)
{
    public int Number { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Body { get; init; } = string.Empty;

    public Option<Uri> Link { get; init; }

    public string Author { get; init; } = string.Empty;

    public Option<string> Label { get; init; }

    public Option<string> Comment { get; init; }

    public Option<string> Branch { get; init; }

    public Option<string> Head { get; init; }

    public Option<string> Reviewer { get; init; }

    public IReadOnlyList<CheckRun> Checks { get; init; } = [];

    public bool PullRequest { get; init; }

    public bool FromFork { get; init; }
}

public enum ForgeEventVerdict
{
    Fired,
    NotAllowed,
    Duplicate,
    Filtered,
}

public enum FilterReason
{
    OtherLabel,
    OtherCommand,
    OtherReviewer,
    FromFork,
    TriggerGone,
}

public sealed record ForgeEventEntry(TriggerId Trigger, DateTimeOffset At, ForgeEventKind Kind, string Id, string Actor, ForgeEventVerdict Verdict)
{
    public int Number { get; init; }

    public string Title { get; init; } = string.Empty;

    public Option<string> Detail { get; init; }

    public Option<FilterReason> Reason { get; init; }

    public Option<RunOutcome> Outcome { get; init; }

    public Option<JobId> Job { get; init; }
}

public enum EventWatchStatus
{
    Waiting,
    Watching,
    BackingOff,
    Disabled,
}

public sealed record ForgeEventWatch(TriggerId Trigger, ForgeName Connection, ForgeEventKind Kind)
{
    public string Forge { get; init; } = string.Empty;

    public EventWatchStatus Status { get; init; }

    public Option<DateTimeOffset> Baseline { get; init; }

    public Option<DateTimeOffset> LastPolled { get; init; }

    public Option<DateTimeOffset> NextPoll { get; init; }

    public Option<ForgeError> Failure { get; init; }

    public int Failures { get; init; }
}

public interface IForgeEventLog
{
    IReadOnlyList<ForgeEventWatch> Watches();

    IReadOnlyList<ForgeEventEntry> Events();

    ValueTask<Result<ForgeEventWatch, ForgeError>> CheckNowAsync(TriggerId trigger, CancellationToken cancellationToken);
}

public sealed record ForgeEventsChanged(DateTimeOffset At) : IIntegrationEvent;
