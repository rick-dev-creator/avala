using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Workbench.Board;
using Avala.Workbench.Conversation;
using Avala.Workbench.Steering;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class ComposerViewModelTests
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
    public async Task ARefusedMessageStaysInTheDraftWithTheReason()
    {
        var composer = Composer(JobStatus.NeedsHelp);
        jobs.Refusal = JobRejection.WorkspaceUnavailable;
        composer.Draft = "Use the staging database";

        await composer.SendCommand.ExecuteAsync(null);

        Assert.Equal(("Use the staging database", "The job's worktree is gone."), (composer.Draft, composer.Error));
    }

    [Fact]
    public void ARunningJobCanBeInterruptedButTakesNoMessage()
    {
        var composer = Composer(JobStatus.Running);
        composer.Draft = "Hurry";

        Assert.Equal((false, true, true), (composer.SendCommand.CanExecute(null), composer.InterruptCommand.CanExecute(null), composer.StopCommand.CanExecute(null)));
    }

    [Fact]
    public void AnEmptyDraftCannotBeSent()
    {
        var composer = Composer(JobStatus.NeedsHelp);
        composer.Draft = "  ";

        Assert.False(composer.SendCommand.CanExecute(null));
    }

    private ComposerViewModel Composer(JobStatus status)
    {
        var summary = new FakeCatalog().Add("Fix the failing test", status).Summary;
        board.Publish(ImmutableDictionary<JobId, BoardJob>.Empty.Add(summary.Job, new BoardJob(summary, Transcript.Empty)));
        var composer = new ComposerViewModel(summary.Job, new JobSteering(jobs, board));
        composer.Track(status);

        return composer;
    }
}
