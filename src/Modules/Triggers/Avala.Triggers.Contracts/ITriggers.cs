using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Triggers.Contracts;

public interface ITriggers
{
    WebhookEndpoint Endpoint { get; }

    ValueTask<IReadOnlyList<TriggerState>> ListAsync(CancellationToken cancellationToken);

    IReadOnlyList<TriggerRun> RunsOf(TriggerId trigger);

    IReadOnlyList<WebhookDelivery> Deliveries();

    IReadOnlyList<TriggerFile> Files();

    ValueTask<Result<TriggerRun, TriggerError>> FireAsync(TriggerId trigger, FireRequest request, CancellationToken cancellationToken);

    ValueTask<Result<TriggerId, TriggerError>> EnableAsync(TriggerId trigger, bool enabled, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<TriggerFile>> ReloadAsync(CancellationToken cancellationToken);
}

public readonly record struct TriggerId(string Scope, string Name)
{
    public const string Machine = "machine";

    public string Key => $"{Scope}#{Name}";
}

public enum TriggerKind
{
    Interval,
    FixedTime,
    Webhook,
}

public enum TriggerTarget
{
    Job,
    Loop,
}

public enum CatchUp
{
    None,
    Once,
}

public enum TriggerOrigin
{
    Schedule,
    CatchUp,
    Webhook,
    Manual,
    External,
}

public enum RunOutcome
{
    Submitted,
    Enqueued,
    Rejected,
    AtConcurrencyCap,
    Missed,
    Dropped,
}

public enum DeliveryVerdict
{
    Accepted,
    NotPost,
    UnknownTrigger,
    TooLarge,
    RateLimited,
    NotSigned,
    NoSecret,
    BadSignature,
    Stale,
    Replayed,
    Malformed,
    Disabled,
}

public enum TriggerFileStatus
{
    Absent,
    Applied,
    Rejected,
}

public enum TriggerError
{
    Unreadable,
    TooLarge,
    Malformed,
    UnknownField,
    InvalidKey,
    DuplicateKey,
    MissingInstruction,
    InvalidSchedule,
    InvalidInterval,
    InvalidTime,
    InvalidDays,
    InvalidCatchUp,
    InvalidAutonomy,
    InvalidTarget,
    InvalidAttempts,
    InvalidConcurrency,
    InvalidRate,
    InvalidSecret,
    InvalidTemplate,
    WebhookNotAllowed,
    MissingRepository,
    UnknownTrigger,
    NoPortLease,
    ListenerFailed,
}

public sealed record TriggerState(TriggerId Id, string Repository, TriggerKind Kind, TriggerTarget Target, Autonomy Autonomy, int Concurrency, bool Enabled)
{
    public int EveryMinutes { get; init; }

    public Option<TimeOnly> At { get; init; }

    public IReadOnlyList<DayOfWeek> Days { get; init; } = [];

    public CatchUp CatchUp { get; init; }

    public Option<string> Hook { get; init; }

    public Option<DateTimeOffset> NextRun { get; init; }

    public Option<DateTimeOffset> LastRun { get; init; }

    public int Running { get; init; }
}

public sealed record FireRequest(TriggerOrigin Origin, string Who)
{
    public Option<string> Payload { get; init; }

    public Option<Guid> Delivery { get; init; }
}

public sealed record TriggerRun(Guid Id, TriggerId Trigger, TriggerOrigin Origin, string Who, DateTimeOffset At, RunOutcome Outcome)
{
    public Option<JobId> Job { get; init; }

    public Option<JobRejection> Rejection { get; init; }

    public Option<string> PayloadDigest { get; init; }

    public Option<Guid> Delivery { get; init; }

    public Autonomy Asked { get; init; }

    public Autonomy Applied { get; init; }

    public int Missed { get; init; }

    public bool AutonomyCapped => Applied < Asked;
}

public sealed record WebhookDelivery(Guid Id, DateTimeOffset At, string Path, DeliveryVerdict Verdict)
{
    public Option<TriggerId> Trigger { get; init; }

    public Option<string> Digest { get; init; }

    public Option<string> Nonce { get; init; }

    public Option<Guid> Run { get; init; }
}

public sealed record TriggerFile(string Path, TriggerFileStatus Status)
{
    public Option<string> Repository { get; init; }

    public Option<TriggerError> Error { get; init; }

    public int Triggers { get; init; }
}

public sealed record WebhookEndpoint(Option<string> Url, Option<TriggerError> Problem)
{
    public static WebhookEndpoint Off { get; } = new(Option<string>.None, Option<TriggerError>.None);
}

public sealed record TriggerFired(TriggerRun Run) : IIntegrationEvent;

public sealed record WebhookReceived(WebhookDelivery Delivery) : IIntegrationEvent;

public sealed record TriggersChanged(DateTimeOffset At) : IIntegrationEvent;
