using Avala.Delegation.Contracts;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Transcripts.Contracts;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Board;

internal sealed class TranscriptRecall(ITranscripts transcripts, IHandoffs handoffs, IDelegations delegations)
{
    public async Task<Transcript> RecalledAsync(JobHistory history, bool restored, CancellationToken cancellationToken)
    {
        var (job, instruction, attempts) = (history.Summary.Job, history.Summary.Instruction, history.Attempts);
        var transcript = restored && attempts.Count > 0
            ? Recollection.Recalled(instruction, attempts, await transcripts.EarlierRunsAsync(job, cancellationToken))
            : Transcript.Empty.WithPrompts(instruction, attempts);
        var moved = handoffs.OfJob(job).Aggregate(transcript, (noted, handoff) => noted.WithHandoff(handoff));

        return delegations.OfParent(job).OrderBy(delegation => delegation.At).Aggregate(moved, (noted, delegation) => noted.Delegated(delegation));
    }
}
