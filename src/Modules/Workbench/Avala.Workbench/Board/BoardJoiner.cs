using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Transcripts.Contracts;
using Avala.Verification.Contracts;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Board;

internal sealed class BoardJoiner(IVerifications verifications, ITranscripts transcripts)
{
    public async Task<BoardJob> JoinedAsync(JobHistory history, bool restored, CancellationToken cancellationToken)
    {
        var (instruction, attempts) = (history.Summary.Instruction, history.Attempts);
        var transcript = restored && attempts.Count > 0
            ? Recollection.Recalled(instruction, attempts, await transcripts.EarlierRunsAsync(history.Summary.Job, cancellationToken))
            : Transcript.Empty.WithPrompts(instruction, attempts);
        var verified = verifications.OfJob(history.Summary.Job);

        return new BoardJob(history.Summary, transcript)
        {
            Attempts = attempts.Count,
            Verification = verified.Count > 0 ? verified[^1] : Option<VerificationReport>.None,
            Choice = history.Choice,
        };
    }
}
