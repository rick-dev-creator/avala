using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Steering;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Steering;

public sealed class JobSteeringTests
{
    private readonly FakeJobs jobs = new();
    private readonly JobBoard board = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("NeedsHelp", "continue Use the staging database")]
    [InlineData("AwaitingReview", "send back Use the staging database")]
    public async Task AMessageContinuesAJobThatNeedsYouAndSendsBackAJobAwaitingReview(string status, string call)
    {
        var job = OnBoard(Enum.Parse<JobStatus>(status));

        Outcomes.Succeeds(await new JobSteering(jobs, board).SendAsync(job, "Use the staging database", Cancellation));

        Assert.Equal([call], jobs.Calls);
    }

    [Fact]
    public async Task AMessageToAJobThatIsWorkingIsRefusedWithoutAskingTheJob()
    {
        var job = OnBoard(JobStatus.Running);

        Assert.Equal(JobRejection.NotHeld, Outcomes.FailsWith(await new JobSteering(jobs, board).SendAsync(job, "Hurry", Cancellation)));
        Assert.Empty(jobs.Calls);
    }

    [Fact]
    public async Task InterruptingAndStoppingBothHoldTheJobWithTheirOwnReasonAndNeitherDiscardsIt()
    {
        var job = OnBoard(JobStatus.Running);
        var steering = new JobSteering(jobs, board);

        Outcomes.Succeeds(await steering.InterruptAsync(job, Cancellation));
        Outcomes.Succeeds(await steering.StopAsync(job, Cancellation));

        Assert.Equal(["hold Interrupted", "hold Stopped"], jobs.Calls);
    }

    [Theory]
    [InlineData("Running", false, true, true, false, true)]
    [InlineData("NeedsHelp", true, false, false, true, true)]
    [InlineData("AwaitingReview", true, false, false, true, true)]
    [InlineData("Checking", false, false, false, false, true)]
    [InlineData("Approved", false, false, false, false, false)]
    public void WhatAJobAcceptsDependsOnItsStatus(string status, bool messages, bool interrupt, bool stop, bool review, bool discard)
    {
        var parsed = Enum.Parse<JobStatus>(status);

        Assert.Equal(
            (messages, interrupt, stop, review, discard),
            (parsed.AcceptsMessages, parsed.CanBeInterrupted, parsed.CanBeStopped, parsed.CanBeReviewed, parsed.CanBeDiscarded));
    }

    private JobId OnBoard(JobStatus status)
    {
        var summary = new FakeCatalog().Add("Fix the failing test", status).Summary;
        board.Publish(ImmutableDictionary<JobId, BoardJob>.Empty.Add(summary.Job, new BoardJob(summary, Transcript.Empty)));

        return summary.Job;
    }
}
