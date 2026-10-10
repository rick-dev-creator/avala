using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Transcripts.Contracts;
using Avala.Verification.Contracts;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Board;

internal sealed class BoardJoiner(IVerifications verifications, ITranscripts transcripts, IHandoffs handoffs)
{
    public async Task<BoardJob> JoinedAsync(JobHistory history, bool restored, CancellationToken cancellationToken)
    {
        var (instruction, attempts) = (history.Summary.Instruction, history.Attempts);
        var transcript = restored && attempts.Count > 0
            ? Recollection.Recalled(instruction, attempts, await transcripts.EarlierRunsAsync(history.Summary.Job, cancellationToken))
            : Transcript.Empty.WithPrompts(instruction, attempts);
        var verified = verifications.OfJob(history.Summary.Job);
        var moved = handoffs.OfJob(history.Summary.Job);

        return new BoardJob(history.Summary, moved.Aggregate(transcript, (noted, handoff) => noted.WithHandoff(handoff)))
        {
            Handoffs = moved,
            Wait = history.Summary.Status == JobStatus.NeedsHelp ? handoffs.WaitOf(history.Summary.Job) : Option<ResetWait>.None,
            Attempts = attempts.Count,
            Verification = verified.Count > 0 ? verified[^1] : Option<VerificationReport>.None,
            Choice = history.Choice,
        };
    }
}
