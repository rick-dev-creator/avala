using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Forges.Contracts;

public sealed record PullRequestDelivery
{
    public const string Strategy = "pull-request";
}

public readonly record struct ForgeName(string Value);

public enum OnPullRequest
{
    WatchOnly,
    WakeOnCi,
    WakeOnCiAndReviews,
}

public enum Redelivery
{
    Automatic,
    Review,
}

public enum WatchStatus
{
    Watching,
    BackingOff,
    WaitingForJob,
    NeedsPerson,
    Ended,
}

public enum WatchEnd
{
    Merged,
    Closed,
    Green,
    JobEnded,
}

public enum WakeReason
{
    ChecksFailed,
    ChangesRequested,
    Conflict,
}

public enum WakeOutcome
{
    Woken,
    Refused,
    HeldForPerson,
}

public sealed record PullRequestWatchState(JobId Job, ForgeName Forge, PullRequestRef PullRequest, OnPullRequest Policy, int MaxWakeUps)
{
    public WatchStatus Status { get; init; } = WatchStatus.Watching;

    public Redelivery Redelivery { get; init; }

    public int WakeUps { get; init; }

    public Option<PullRequestState> Last { get; init; }

    public Option<DateTimeOffset> LastPolled { get; init; }

    public Option<DateTimeOffset> NextPoll { get; init; }

    public Option<ForgeError> Failure { get; init; }

    public int Failures { get; init; }

    public Option<WakeReason> Pending { get; init; }

    public Option<WatchEnd> Ended { get; init; }
}

public sealed record WakeUpRecord(JobId Job, int PullRequest, WakeReason Reason, string Key, string Feedback, DateTimeOffset At, WakeOutcome Outcome)
{
    public Option<ContinuedIn> Conversation { get; init; }

    public Option<JobRejection> Refusal { get; init; }
}

public sealed record PullRequestOffer(ForgeName Forge, string ForgeKind, string Remote);

public interface IPullRequests
{
    ValueTask<Option<PullRequestOffer>> OfferAsync(JobId job, CancellationToken cancellationToken);

    Option<PullRequestWatchState> Of(JobId job);

    IReadOnlyList<WakeUpRecord> WakeUpsOf(JobId job);

    Option<ForgeError> RefusalOf(JobId job);

    ValueTask<Result<PullRequestWatchState, ForgeError>> RefreshAsync(JobId job, CancellationToken cancellationToken);
}

public enum CredentialSource
{
    None,
    Environment,
    Cli,
}

public sealed record ForgeConnectionInfo(ForgeName Name, string Forge, Option<Uri> Url, CredentialSource Source, Option<string> Reference)
{
    public Option<ForgeError> Problem { get; init; }
}

public sealed record ForgeCatalog(IReadOnlyList<ForgeInfo> Forges, IReadOnlyList<ForgeConnectionInfo> Connections, Option<ForgeError> FileError, TimeSpan PollInterval);

public interface IForgeCatalog
{
    ValueTask<ForgeCatalog> CatalogAsync(CancellationToken cancellationToken);
}

public sealed record PullRequestOpened(JobId Job, ForgeName Forge, PullRequestRef PullRequest) : IIntegrationEvent;

public sealed record PullRequestNotOpened(JobId Job, ForgeError Error) : IIntegrationEvent;

public sealed record PullRequestWatchChanged(PullRequestWatchState State) : IIntegrationEvent;

public sealed record PullRequestWakeUp(WakeUpRecord WakeUp) : IIntegrationEvent;
