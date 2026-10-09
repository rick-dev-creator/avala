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
    public void ARunningJobCanBeInterruptedOrStoppedButTakesNoMessage()
    {
        var composer = Composer(JobStatus.Running);
        composer.Draft = "Hurry";

        Assert.Equal((false, true, true, false), (composer.SendCommand.CanExecute(null), composer.InterruptCommand.CanExecute(null), composer.StopCommand.CanExecute(null), composer.AcceptsMessages));
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
                composer.Track(JobStatus.NeedsHelp);
            })
            .ThenNotified(nameof(ComposerViewModel.Status), nameof(ComposerViewModel.AcceptsMessages), nameof(ComposerViewModel.Placeholder))
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

    private ComposerViewModel Composer(JobStatus status) => Composer(status, jobs);

    private ComposerViewModel Composer(JobStatus status, IJobs steered)
    {
        var summary = new FakeCatalog().Add("Fix JPY rounding in invoice totals", status).Summary;
        board.Publish(ImmutableDictionary<JobId, BoardJob>.Empty.Add(summary.Job, new BoardJob(summary, Transcript.Empty)));
        var composer = new ComposerViewModel(summary.Job, new JobSteering(steered, board));
        composer.Track(status);

        return composer;
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

        public ValueTask<Avala.Sdk.Result<JobApproval, JobRejection>> ApproveAsync(JobId job, CancellationToken cancellationToken) => inner.ApproveAsync(job, cancellationToken);

        public ValueTask<Avala.Sdk.Result<JobContinuation, JobRejection>> SendBackAsync(JobId job, string feedback, CancellationToken cancellationToken) => inner.SendBackAsync(job, feedback, cancellationToken);

        public ValueTask<Avala.Sdk.Result<JobContinuation, JobRejection>> ResumeAsync(JobId job, CancellationToken cancellationToken) => inner.ResumeAsync(job, cancellationToken);
    }
}
