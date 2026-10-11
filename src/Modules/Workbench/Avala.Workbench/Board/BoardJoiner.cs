using Avala.Forges.Contracts;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;

namespace Avala.Workbench.Board;

internal sealed class BoardJoiner(IVerifications verifications, TranscriptRecall recall, IHandoffs handoffs, IPullRequests pullRequests)
{
    public async Task<BoardJob> JoinedAsync(JobHistory history, bool restored, CancellationToken cancellationToken)
    {
        var verified = verifications.OfJob(history.Summary.Job);

        return new BoardJob(history.Summary, await recall.RecalledAsync(history, restored, cancellationToken))
        {
            Handoffs = handoffs.OfJob(history.Summary.Job),
            Hold = history.Summary.Status == JobStatus.NeedsHelp ? history.Summary.Hold : Option<HoldReason>.None,
            Wait = history.Summary.Status == JobStatus.NeedsHelp ? handoffs.WaitOf(history.Summary.Job) : Option<ResetWait>.None,
            Attempts = history.Attempts.Count,
            Verification = verified.Count > 0 ? verified[^1] : Option<VerificationReport>.None,
            Choice = history.Choice,
            PullRequest = pullRequests.Of(history.Summary.Job),
            WakeUps = pullRequests.WakeUpsOf(history.Summary.Job),
        };
    }
}
