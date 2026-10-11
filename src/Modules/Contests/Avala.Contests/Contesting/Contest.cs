using System.Collections.Immutable;
using Avala.Agents.Contracts.Connections;
using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Domain;
using Stateless;

namespace Avala.Contests.Contesting;

internal sealed record CandidateSettlement(ContestId Contest, JobId Job, CandidateOutcome Outcome, bool Changed, ContestStatus Now) : IDomainEvent;

internal sealed record CandidateReturned(ContestId Contest, JobId Job) : IDomainEvent;

internal sealed record VerdictReached(ContestId Contest, Option<JobId> Winner) : IDomainEvent;

internal sealed record ContestPicked(ContestId Contest, JobId Picked, IReadOnlyList<JobId> Losers) : IDomainEvent;

internal sealed class Contest : IAggregateRoot<ContestId>
{
    private static readonly CandidateOutcome[] Ended = [CandidateOutcome.Failed, CandidateOutcome.Discarded, CandidateOutcome.Approved];

    private readonly StateMachine<ContestStatus, ContestTrigger> machine;

    private ImmutableList<CandidateRecord> candidates = [];

    private Contest(ContestId id, string repository, string instruction, DateTimeOffset started)
    {
        Id = id;
        Repository = repository;
        Instruction = instruction;
        Started = started;
        machine = ContestLifecycle.Create(() => Status, status => Status = status);
    }

    public ContestId Id { get; }

    public string Repository { get; }

    public string Instruction { get; }

    public DateTimeOffset Started { get; }

    public ContestStatus Status { get; private set; } = ContestStatus.Running;

    public bool Spread { get; private init; }

    public int Round { get; private set; }

    public Option<ContestVerdict> Verdict { get; private set; }

    public Option<JobId> Picked { get; private set; }

    public IReadOnlyList<CandidateRecord> Candidates => candidates;

    public ContestRecord Record =>
        new(Id, Repository, Instruction, candidates, Status, Started)
        {
            Spread = Spread,
            Round = Round,
            Verdict = Verdict,
            Picked = Picked,
        };

    public static Result<int, ContestError> Sized(int candidates, int max) =>
        candidates < 2 ? ContestError.TooFewCandidates
        : candidates > Math.Min(max, BestOf.Limit) ? ContestError.TooManyCandidates
        : candidates;

    public static Result<Contest, ContestError> Start(
        ContestId id,
        string repository,
        string instruction,
        IReadOnlyList<(JobId Job, Option<ConnectionName> Connection)> entered,
        int max,
        DateTimeOffset started,
        bool spread = false) =>
        Sized(entered.Count, max).Map(_ => new Contest(id, repository, instruction, started)
        {
            Spread = spread,
            candidates = [.. entered.Select((entry, ordinal) => new CandidateRecord(entry.Job, ordinal + 1, entry.Connection, CandidateOutcome.Running, Option<DateTimeOffset>.None))],
        });

    public static Result<Contest, ContestError> Restore(ContestRecord record) =>
        Sized(record.Candidates.Count, BestOf.Limit).Map(_ => new Contest(record.Contest, record.Repository, record.Instruction, record.Started)
        {
            candidates = [.. record.Candidates],
            Status = record.Status,
            Spread = record.Spread,
            Round = record.Round,
            Verdict = record.Verdict,
            Picked = record.Picked,
        });

    public Result<CandidateSettlement, ContestError> Settle(JobId job, CandidateOutcome outcome, DateTimeOffset at) =>
        Find(job).Match(candidate => Settle(candidate, outcome, at), () => ContestError.UnknownCandidate);

    public Result<CandidateReturned, ContestError> Reopen(JobId job) =>
        Find(job).Match(Reopen, () => ContestError.UnknownCandidate);

    public Result<VerdictReached, ContestError> Decide(int round, ContestVerdict verdict)
    {
        if (Status != ContestStatus.Judging)
        {
            return ContestError.CannotDecide;
        }

        if (round != Round)
        {
            return ContestError.StaleVerdict;
        }

        return machine.TryFire(verdict.Winner.IsSome ? ContestTrigger.Decide : ContestTrigger.FindNone, ContestError.CannotDecide).Map(_ =>
        {
            Verdict = verdict;

            return new VerdictReached(Id, verdict.Winner);
        });
    }

    public Result<ContestPicked, ContestError> Close(JobId picked, DateTimeOffset at) =>
        Find(picked).Match(
            candidate => machine.TryFire(ContestTrigger.Close, ContestError.CannotClose).Map(_ =>
            {
                candidates = candidates.Replace(candidate, candidate with { Outcome = CandidateOutcome.Approved, Settled = at });
                Picked = picked;

                return new ContestPicked(Id, picked, [.. candidates.Where(found => !Ended.Contains(found.Outcome)).Select(found => found.Job)]);
            }),
            () => ContestError.UnknownCandidate);

    private Result<CandidateSettlement, ContestError> Settle(CandidateRecord candidate, CandidateOutcome outcome, DateTimeOffset at)
    {
        var job = candidate.Job;

        if (outcome is CandidateOutcome.Running or CandidateOutcome.Approved || Status is ContestStatus.Closed or ContestStatus.Cancelled)
        {
            return ContestError.CannotSettle;
        }

        if (candidate.Outcome == outcome)
        {
            return new CandidateSettlement(Id, job, outcome, Changed: false, Status);
        }

        candidates = candidates.Replace(candidate, candidate with { Outcome = outcome, Settled = at });
        var next = !candidates.All(found => found.Outcome != CandidateOutcome.Running) ? Option<ContestTrigger>.None
            : candidates.All(found => Ended.Contains(found.Outcome)) ? ContestTrigger.Cancel
            : Status == ContestStatus.Running ? ContestTrigger.Judge
            : ContestTrigger.Rejudge;

        return next.Match(
            trigger => machine.TryFire(trigger, ContestError.CannotSettle).Map(now =>
            {
                Round += trigger == ContestTrigger.Cancel ? 0 : 1;

                return new CandidateSettlement(Id, job, outcome, Changed: true, now);
            }),
            () => new CandidateSettlement(Id, job, outcome, Changed: true, Status));
    }

    private Result<CandidateReturned, ContestError> Reopen(CandidateRecord candidate)
    {
        if (candidate.Outcome is CandidateOutcome.Running or CandidateOutcome.Failed or CandidateOutcome.Discarded or CandidateOutcome.Approved)
        {
            return ContestError.CannotReopen;
        }

        return machine.TryFire(ContestTrigger.Reopen, ContestError.CannotReopen).Map(_ =>
        {
            candidates = candidates.Replace(candidate, candidate with { Outcome = CandidateOutcome.Running, Settled = Option<DateTimeOffset>.None });
            Verdict = Option<ContestVerdict>.None;

            return new CandidateReturned(Id, candidate.Job);
        });
    }

    private Option<CandidateRecord> Find(JobId job) => candidates.FirstOrDefault(candidate => candidate.Job == job).ToOption();
}
