using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Contests.Contracts;

public enum ContestStatus
{
    Running,
    Judging,
    Decided,
    Undecided,
    Closed,
    Cancelled,
}

public enum CandidateOutcome
{
    Running,
    AwaitingReview,
    NeedsHelp,
    Failed,
    Discarded,
    Approved,
}

public enum Criterion
{
    Verification,
    RuleFiles,
    Exceptions,
    Attempts,
    DiffSize,
    Cost,
    Finish,
    Order,
}

public sealed record CandidateRecord(JobId Job, int Ordinal, Option<ConnectionName> Connection, CandidateOutcome Outcome, Option<DateTimeOffset> Settled);

public sealed record CandidateFacts(JobId Job, int Ordinal, bool Passed, bool RuleFileEdited, int Exceptions, int Attempts, int LinesChanged, DateTimeOffset Finished)
{
    public IReadOnlyList<Cost> Spent { get; init; } = [];
}

public sealed record Standing(int Place, CandidateFacts Facts)
{
    public Option<Criterion> Excluded { get; init; }

    public Option<Criterion> Ahead { get; init; }
}

public sealed record ContestVerdict(IReadOnlyList<Standing> Ranking, Option<JobId> Winner, DateTimeOffset At);

public sealed record ContestRecord(
    ContestId Contest,
    string Repository,
    string Instruction,
    IReadOnlyList<CandidateRecord> Candidates,
    ContestStatus Status,
    DateTimeOffset Started)
{
    public bool Spread { get; init; }

    public int Round { get; init; }

    public Option<ContestVerdict> Verdict { get; init; }

    public Option<JobId> Picked { get; init; }
}

public sealed record ContestStarted(ContestRecord Contest) : IIntegrationEvent;

public sealed record CandidateSettled(ContestRecord Contest, JobId Candidate) : IIntegrationEvent;

public sealed record CandidateReopened(ContestRecord Contest, JobId Candidate) : IIntegrationEvent;

public sealed record ContestDecided(ContestRecord Contest) : IIntegrationEvent;

public sealed record ContestUndecided(ContestRecord Contest) : IIntegrationEvent;

public sealed record ContestClosed(ContestRecord Contest) : IIntegrationEvent;

public sealed record ContestsRestored(IReadOnlyList<ContestRecord> Contests) : IIntegrationEvent;
