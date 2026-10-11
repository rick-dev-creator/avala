using System.Collections.Immutable;
using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk.Events;

namespace Avala.Workbench.Board;

internal sealed record BoardContest(ContestRecord Contest, int Revision);

internal sealed class ContestKeeper(JobBoard board) :
    IHandle<ContestsRestored>,
    IHandle<ContestStarted>,
    IHandle<CandidateSettled>,
    IHandle<CandidateReopened>,
    IHandle<ContestDecided>,
    IHandle<ContestUndecided>,
    IHandle<ContestClosed>
{
    private ImmutableDictionary<JobId, BoardContest> contests = ImmutableDictionary<JobId, BoardContest>.Empty;
    private int revision;

    public ValueTask HandleAsync(ContestsRestored integrationEvent, CancellationToken cancellationToken) => KeepAsync([.. integrationEvent.Contests]);

    public ValueTask HandleAsync(ContestStarted integrationEvent, CancellationToken cancellationToken) => KeepAsync(integrationEvent.Contest);

    public ValueTask HandleAsync(CandidateSettled integrationEvent, CancellationToken cancellationToken) => KeepAsync(integrationEvent.Contest);

    public ValueTask HandleAsync(CandidateReopened integrationEvent, CancellationToken cancellationToken) => KeepAsync(integrationEvent.Contest);

    public ValueTask HandleAsync(ContestDecided integrationEvent, CancellationToken cancellationToken) => KeepAsync(integrationEvent.Contest);

    public ValueTask HandleAsync(ContestUndecided integrationEvent, CancellationToken cancellationToken) => KeepAsync(integrationEvent.Contest);

    public ValueTask HandleAsync(ContestClosed integrationEvent, CancellationToken cancellationToken) => KeepAsync(integrationEvent.Contest);

    private ValueTask KeepAsync(params ContestRecord[] kept)
    {
        revision++;
        contests = contests.SetItems(kept.SelectMany(contest => contest.Candidates.Select(candidate => KeyValuePair.Create(candidate.Job, new BoardContest(contest, revision)))));
        board.Publish(contests);

        return ValueTask.CompletedTask;
    }
}
