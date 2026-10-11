using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Notifications.Contracts;

public interface INotifications
{
    ValueTask<NotificationFile> ReadAsync(CancellationToken cancellationToken);

    ValueTask<Result<NotificationFile, NotificationError>> SetUpAsync(CancellationToken cancellationToken);

    ValueTask<Result<NotificationFile, NotificationError>> ChooseAsync(NotificationChoices choices, CancellationToken cancellationToken);

    ValueTask<Result<NotificationRecord, NotificationError>> SendTestAsync(CancellationToken cancellationToken);

    IReadOnlyList<NotificationRecord> Recent();
}

public sealed record NotificationTransport
{
    public const string Handler = "avala.notifications";
}

public enum NotificationError
{
    Unreadable,
    TooLarge,
    Malformed,
    UnknownField,
    InvalidChannel,
    MissingChannel,
    InvalidAddress,
    InsecureAddress,
    InvalidTopic,
    InvalidSecret,
    InvalidEvents,
    InvalidDetail,
    InvalidQuietHours,
    Unwritable,
    AlreadySetUp,
    NotSetUp,
    Disabled,
    MissingSecret,
    Unreachable,
    Refused,
}

public enum NotificationFileStatus
{
    Absent,
    Applied,
    Rejected,
}

public enum ChannelKind
{
    Ntfy,
    Webhook,
}

public enum NotificationKind
{
    Decision,
    Held,
    Budget,
    Failed,
    LoopEnded,
    Review,
    Summary,
    Digest,
    Test,
}

public enum Severity
{
    Info,
    Urgent,
}

public enum DetailLevel
{
    Minimal,
    Detailed,
}

public enum SecretStatus
{
    None,
    Set,
    NotSet,
}

public enum NotificationOutcome
{
    Sent,
    Failed,
    Withdrawn,
    Present,
    Duplicate,
    Capped,
    Quiet,
}

public sealed record NtfyTarget(Uri Server, string Topic)
{
    public static Uri DefaultServer { get; } = new("https://ntfy.sh");

    public Option<string> TokenEnv { get; init; }
}

public sealed record WebhookTarget(Uri Url)
{
    public Option<string> SecretEnv { get; init; }
}

public sealed record QuietHours(TimeOnly From, TimeOnly To)
{
    public IReadOnlyList<DayOfWeek> Days { get; init; } = [];
}

public sealed record NotificationSettings(ChannelKind Channel)
{
    public static IReadOnlyList<NotificationKind> Chosen { get; } =
        [NotificationKind.Decision, NotificationKind.Held, NotificationKind.Budget, NotificationKind.Failed, NotificationKind.LoopEnded];

    public static IReadOnlyList<NotificationKind> Choosable { get; } = [.. Chosen, NotificationKind.Review];

    public bool Enabled { get; init; } = true;

    public Option<NtfyTarget> Ntfy { get; init; }

    public Option<WebhookTarget> Webhook { get; init; }

    public IReadOnlyList<NotificationKind> Events { get; init; } = Chosen;

    public DetailLevel Detail { get; init; }

    public Option<QuietHours> Quiet { get; init; }

    public Option<string> SecretVariable =>
        Channel == ChannelKind.Ntfy ? Ntfy.Bind(target => target.TokenEnv) : Webhook.Bind(target => target.SecretEnv);
}

public sealed record NotificationChoices(IReadOnlyList<NotificationKind> Events, DetailLevel Detail);

public sealed record NotificationFile(string Path, NotificationFileStatus Status)
{
    public Option<NotificationError> Error { get; init; }

    public Option<NotificationSettings> Settings { get; init; }

    public SecretStatus Secret { get; init; }

    public string Preview { get; init; } = string.Empty;
}

public sealed record NotificationRecord(Guid Id, NotificationKind Kind, Severity Severity, DateTimeOffset At, NotificationOutcome Outcome)
{
    public Option<JobId> Job { get; init; }

    public string Repository { get; init; } = string.Empty;

    public int Count { get; init; } = 1;

    public int Attempts { get; init; }

    public Option<NotificationError> Error { get; init; }
}

public sealed record NotificationSettled(NotificationRecord Record) : IIntegrationEvent;

public sealed record NotificationSettingsChanged(NotificationFile File) : IIntegrationEvent;
