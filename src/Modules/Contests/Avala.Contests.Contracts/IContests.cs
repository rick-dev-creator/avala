using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Contests.Contracts;

public readonly record struct ContestId(Guid Value)
{
    public static ContestId New() => new(Guid.CreateVersion7());
}

public interface IContests
{
    ValueTask<Result<ContestRecord, ContestRejection>> StartAsync(ContestRequest request, CancellationToken cancellationToken);

    ValueTask<Result<BestOf, ContestRejection>> BestOfAsync(string repository, CancellationToken cancellationToken);

    Option<ContestRecord> Of(ContestId contest);

    Option<ContestRecord> OfJob(JobId job);

    IReadOnlyList<ContestRecord> All();
}

public sealed record ContestRequest(JobRequest Job, int Candidates)
{
    public bool Spread { get; init; }
}

public sealed record BestOf(int Candidates, int Max)
{
    public const int Limit = 8;

    public static BestOf Default { get; } = new(1, 4);
}

public enum ContestRejection
{
    Unreadable,
    TooLarge,
    Malformed,
    UnknownField,
    InvalidCandidates,
    InvalidMax,
    TooFewCandidates,
    TooManyCandidates,
    NotSubmitted,
}
