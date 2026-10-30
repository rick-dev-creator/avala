using Avala.Forges.Contracts;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Board;

internal sealed class JobSurroundings(IHandoffs handoffs, IPullRequests pullRequests, IWorkspaces workspaces)
{
    public IHandoffs Handoffs => handoffs;

    public IPullRequests PullRequests => pullRequests;

    public async Task<bool> WorktreeGoneAsync(JobSummary summary, CancellationToken cancellationToken) =>
        summary.Status == JobStatus.Approved
        && await summary.Workspace.Match(
            async workspace => (await workspaces.FindAsync(workspace, cancellationToken)).Match(_ => false, failure => failure == WorkspaceFailure.UnknownWorkspace),
            () => Task.FromResult(false));
}

internal sealed class BoardJoiner(IVerifications verifications, TranscriptRecall recall, JobSurroundings surroundings)
{
    public async Task<BoardJob> JoinedAsync(JobHistory history, bool restored, CancellationToken cancellationToken)
    {
        var (handoffs, pullRequests) = (surroundings.Handoffs, surroundings.PullRequests);
        var verified = verifications.OfJob(history.Summary.Job);
        var watch = pullRequests.Of(history.Summary.Job);
        var wakeUps = pullRequests.WakeUpsOf(history.Summary.Job);
        var recalled = await recall.RecalledAsync(history, restored, cancellationToken);

        return new BoardJob(history.Summary, recalled.WithCi(watch, wakeUps))
        {
            Handoffs = handoffs.OfJob(history.Summary.Job),
            Marks = handoffs.MarksOf(history.Summary.Job),
            Hold = history.Summary.Status == JobStatus.NeedsHelp ? history.Summary.Hold : Option<HoldReason>.None,
            Wait = history.Summary.Status == JobStatus.NeedsHelp ? handoffs.WaitOf(history.Summary.Job) : Option<ResetWait>.None,
            Attempts = history.Attempts.Count,
            Verification = verified.Count > 0 ? verified[^1] : Option<VerificationReport>.None,
            Choice = history.Choice,
            PullRequest = watch,
            WakeUps = wakeUps,
            Reclaimed = await surroundings.WorktreeGoneAsync(history.Summary, cancellationToken),
        };
    }
}
