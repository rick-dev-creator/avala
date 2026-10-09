using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Conversation;
using Avala.Workbench.Steering;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class ComposerViewModelScripts
{
    private readonly FakeJobs jobs = new();
    private readonly JobBoard board = new();
    private BoardJob shown = null!;
    private QueuedMessages queue = null!;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASentMessageContinuesTheJobAndClearsTheDraft()
    {
        var composer = Composer(JobStatus.NeedsHelp);
        composer.Draft = " Use the staging database ";

        await composer.SendCommand.ExecuteAsync(null);

        Assert.Equal(["continue Use the staging database"], jobs.Calls);
        Assert.Equal((string.Empty, string.Empty), (composer.Draft, composer.Error));
    }

    [Fact]
    public async Task AMessageToAJobAwaitingReviewSendsItBack()
    {
        var composer = Composer(JobStatus.AwaitingReview);
        composer.Draft = "Also limit by account";

        await composer.SendCommand.ExecuteAsync(null);

        Assert.Equal(["send back Also limit by account"], jobs.Calls);
    }

    [Fact]
    public async Task ARefusedMessageStaysInTheDraftWithTheReason()
    {
        var composer = Composer(JobStatus.NeedsHelp);
        jobs.Refusal = JobRejection.WorkspaceUnavailable;
        composer.Draft = "Use the staging database";

        await composer.SendCommand.ExecuteAsync(null);

        Assert.Equal(("Use the staging database", "The job's worktree is gone."), (composer.Draft, composer.Error));
    }

    [Fact]
    public async Task AMessageToARunningAgentThatTakesMessagesMidTurnJoinsTheRunningTurn()
    {
        var composer = Composer(JobStatus.Running, takesMessagesMidTurn: true);
        composer.Draft = "Keep the old namespace as an alias";

        await composer.SendCommand.ExecuteAsync(null);

        Assert.Equal(["steer Keep the old namespace as an alias"], jobs.Calls);
        Assert.Equal((string.Empty, string.Empty, string.Empty), (composer.Draft, composer.Queued, composer.Error));
        Assert.Equal(("Message the agent while it works…", true, true), (composer.Placeholder, composer.InterruptCommand.CanExecute(null), composer.StopCommand.CanExecute(null)));
    }

    [Fact]
    public async Task AMessageToARunningAgentThatTakesNoMessageMidTurnIsQueuedHonestly()
    {
        var composer = Composer(JobStatus.Running);
        composer.Draft = "Keep the old namespace as an alias";

        await composer.SendCommand.ExecuteAsync(null);

        Assert.Empty(jobs.Calls);
        Assert.Equal((string.Empty, "Keep the old namespace as an alias", true), (composer.Draft, composer.Queued, composer.WithdrawCommand.CanExecute(null)));
        Assert.Equal(("This agent takes no message mid-turn · queue one for when it stops…", "Queue for when the agent stops (Ctrl+Enter)"), (composer.Placeholder, composer.SendHint));
    }

    [Fact]
    public async Task AnAgentThatRefusesTheMidTurnMessageGetsItQueuedInstead()
    {
        var composer = Composer(JobStatus.Running, takesMessagesMidTurn: true);
        jobs.Refusal = JobRejection.NotSteerable;
        composer.Draft = "Keep the alias";

        await composer.SendCommand.ExecuteAsync(null);

        Assert.Equal((string.Empty, "Keep the alias", string.Empty), (composer.Draft, composer.Queued, composer.Error));
    }

    [Fact]
    public async Task QueuedMessagesContinueTheJobTogetherWhenItNextNeedsYou()
    {
        var composer = Composer(JobStatus.Checking);
        composer.Draft = "Keep the alias";
        await composer.SendCommand.ExecuteAsync(null);
        composer.Draft = "And update the changelog";
        await composer.SendCommand.ExecuteAsync(null);

        await queue.HandleAsync(new JobProgressed(shown.Job, JobStatus.NeedsHelp), Cancellation);
        Becomes(composer, JobStatus.NeedsHelp);

        Assert.Equal(["continue Keep the alias\n\nAnd update the changelog"], jobs.Calls);
        Assert.Equal((string.Empty, false), (composer.Queued, composer.WithdrawCommand.CanExecute(null)));
    }

    [Fact]
    public async Task AQueuedMessageWaitsInTheReviewUntilAPersonChoosesToSendItBack()
    {
        var composer = Composer(JobStatus.Running);
        composer.Draft = "Keep the alias";
        await composer.SendCommand.ExecuteAsync(null);

        await queue.HandleAsync(new JobProgressed(shown.Job, JobStatus.AwaitingReview), Cancellation);
        Becomes(composer, JobStatus.AwaitingReview);

        Assert.Empty(jobs.Calls);
        Assert.Equal(("Keep the alias", "Queued · waits in the review: send back with it, or withdraw it"), (composer.Queued, composer.QueuedCaption));
    }

    [Fact]
    public async Task AWithdrawnMessageIsNeverDelivered()
    {
        var composer = Composer(JobStatus.Running);
        composer.Draft = "Keep the alias";
        await composer.SendCommand.ExecuteAsync(null);

        composer.WithdrawCommand.Execute(null);
        await queue.HandleAsync(new JobProgressed(shown.Job, JobStatus.NeedsHelp), Cancellation);

        Assert.Equal(string.Empty, composer.Queued);
        Assert.Empty(jobs.Calls);
    }

    [Fact]
    public async Task AQueuedMessageForAJobThatEndsIsDropped()
    {
        var composer = Composer(JobStatus.Running);
        composer.Draft = "Keep the alias";
        await composer.SendCommand.ExecuteAsync(null);

        await queue.HandleAsync(new JobProgressed(shown.Job, JobStatus.Failed), Cancellation);
        Becomes(composer, JobStatus.Failed);

        Assert.Equal((string.Empty, false), (composer.Queued, composer.SendCommand.CanExecute(null)));
        Assert.Empty(jobs.Calls);
    }

    [Fact]
    public async Task InterruptingAndStoppingHoldTheJobForTheirReasons()
    {
        var composer = Composer(JobStatus.Running);

        await composer.InterruptCommand.ExecuteAsync(null);
        await composer.StopCommand.ExecuteAsync(null);

        Assert.Equal(["hold Interrupted", "hold Stopped"], jobs.Calls);
    }

    [Fact]
    public async Task AStopTheJobRefusesIsReported()
    {
        var composer = Composer(JobStatus.Running);
        jobs.Refusal = JobRejection.NotRunning;

        await composer.StopCommand.ExecuteAsync(null);

        Assert.Equal("The job is not running.", composer.Error);
    }

    [Fact]
    public void AnEmptyDraftCannotBeSent()
    {
        var composer = Composer(JobStatus.NeedsHelp);
        composer.Draft = "  ";

        Assert.False(composer.SendCommand.CanExecute(null));
    }

    [Fact]
    public void AJobThatStopsRunningTakesMessagesInsteadOfInterruptions() =>
        ViewModelScript.Given(Composer(JobStatus.Running))
            .When(composer =>
            {
                composer.Draft = "Carry on";
                Becomes(composer, JobStatus.NeedsHelp);
            })
            .ThenNotified(nameof(ComposerViewModel.Status), nameof(ComposerViewModel.AcceptsMessages), nameof(ComposerViewModel.Placeholder), nameof(ComposerViewModel.SendHint))
            .Then(composer => Assert.Equal((true, false, false), (composer.SendCommand.CanExecute(null), composer.InterruptCommand.CanExecute(null), composer.StopCommand.CanExecute(null))))
            .Then(composer => Assert.Equal("Continue the job with a message…", composer.Placeholder));

    [Fact]
    public async Task ASecondSendWhileTheFirstIsInFlightIsRefusedByTheCommand()
    {
        var gate = new TaskCompletionSource();
        var composer = Composer(JobStatus.NeedsHelp, new GatedJobs(gate.Task, jobs));
        composer.Draft = "Carry on";

        var first = composer.SendCommand.ExecuteAsync(null);
        var again = composer.SendCommand.CanExecute(null);
        gate.SetResult();
        await first;

        Assert.False(again);
        Assert.Single(jobs.Calls);
    }

    private ComposerViewModel Composer(JobStatus status, bool takesMessagesMidTurn = false) => Composer(status, jobs, takesMessagesMidTurn);

    private ComposerViewModel Composer(JobStatus status, IJobs steered, bool takesMessagesMidTurn = false)
    {
        var summary = new FakeCatalog().Add("Fix JPY rounding in invoice totals", status).Summary;
        shown = new BoardJob(summary, Transcript.Empty) { TakesMessagesMidTurn = takesMessagesMidTurn };
        board.Publish(ImmutableDictionary<JobId, BoardJob>.Empty.Add(summary.Job, shown));
        queue = new QueuedMessages(steered);
        var composer = new ComposerViewModel(summary.Job, new JobSteering(steered, board, queue));
        composer.Track(shown);

        return composer;
    }

    private void Becomes(ComposerViewModel composer, JobStatus status)
    {
        shown = shown with { Summary = shown.Summary with { Status = status } };
        board.Publish(ImmutableDictionary<JobId, BoardJob>.Empty.Add(shown.Job, shown));
        composer.Track(shown);
    }

    private sealed class GatedJobs(Task gate, FakeJobs inner) : IJobs
    {
        public ValueTask<Avala.Sdk.Result<JobId, JobRejection>> SubmitAsync(JobRequest request, CancellationToken cancellationToken) => inner.SubmitAsync(request, cancellationToken);

        public ValueTask<Avala.Sdk.Result<JobHold, JobRejection>> HoldAsync(JobId job, HoldReason reason, CancellationToken cancellationToken) => inner.HoldAsync(job, reason, cancellationToken);

        public async ValueTask<Avala.Sdk.Result<JobContinuation, JobRejection>> ContinueAsync(JobId job, string message, CancellationToken cancellationToken)
        {
            await gate;

            return await inner.ContinueAsync(job, message, cancellationToken);
        }

        public ValueTask<Avala.Sdk.Result<JobContinuation, JobRejection>> ContinueOnAsync(
            JobId job,
            Avala.Agents.Contracts.Connections.ConnectionName connection,
            string message,
            CancellationToken cancellationToken) =>
            inner.ContinueOnAsync(job, connection, message, cancellationToken);

        public ValueTask<Avala.Sdk.Result<JobId, JobRejection>> DiscardAsync(JobId job, CancellationToken cancellationToken) => inner.DiscardAsync(job, cancellationToken);

        public ValueTask<Avala.Sdk.Result<JobSteered, JobRejection>> SteerAsync(JobId job, string message, CancellationToken cancellationToken) => inner.SteerAsync(job, message, cancellationToken);

        public ValueTask<Avala.Sdk.Result<JobApproval, JobRejection>> ApproveAsync(JobId job, CancellationToken cancellationToken) => inner.ApproveAsync(job, cancellationToken);

        public ValueTask<Avala.Sdk.Result<JobContinuation, JobRejection>> SendBackAsync(JobId job, string feedback, CancellationToken cancellationToken) => inner.SendBackAsync(job, feedback, cancellationToken);

        public ValueTask<Avala.Sdk.Result<JobContinuation, JobRejection>> ResumeAsync(JobId job, CancellationToken cancellationToken) => inner.ResumeAsync(job, cancellationToken);
    }
}
