using Avala.Jobs.Contracts;
using Avala.Testing;
using Avala.Workbench.Steering;

namespace Avala.Workbench.Tests.Steering;

public sealed class JobSteeringTests : IAsyncDisposable
{
    private readonly FakeJobs jobs = new();
    private readonly QueuedMessages queue;
    private readonly JobSteering steering;
    private readonly JobId job = JobId.New();

    public JobSteeringTests()
    {
        queue = new QueuedMessages(jobs);
        steering = new JobSteering(jobs, queue);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(JobStatus.NeedsHelp, "continue Use the staging database")]
    [InlineData(JobStatus.AwaitingReview, "send back Use the staging database")]
    public async Task AMessageContinuesAJobThatNeedsYouAndSendsBackAJobAwaitingReview(JobStatus status, string call)
    {
        jobs.Status = status;

        Outcomes.Succeeds(await steering.SendAsync(job, status, "Use the staging database", Cancellation));

        Assert.Equal([call], jobs.Calls);
    }

    [Fact]
    public async Task AMessageToAJobThatEndedIsRefusedWithoutAskingTheJob()
    {
        Assert.Equal(JobRejection.NotHeld, Outcomes.FailsWith(await steering.SendAsync(job, JobStatus.Approved, "Hurry", Cancellation)));
        Assert.Empty(jobs.Calls);
    }

    [Theory]
    [InlineData(JobStatus.NeedsHelp, false, "Sent")]
    [InlineData(JobStatus.Running, true, "JoinedTheTurn")]
    [InlineData(JobStatus.Running, false, "Queued")]
    [InlineData(JobStatus.Checking, true, "Queued")]
    [InlineData(JobStatus.Preparing, false, "Queued")]
    public async Task AMessageSaysHowItReachesTheAgent(JobStatus status, bool takesMessagesMidTurn, string delivery)
    {
        (jobs.Status, jobs.Steerable) = (status, takesMessagesMidTurn);

        Assert.Equal(Enum.Parse<MessageDelivery>(delivery), Outcomes.Succeeds(await steering.SendAsync(job, status, "Keep the alias", Cancellation)));
    }

    [Theory]
    [InlineData(JobStatus.Running, JobStatus.NeedsHelp, "Sent", "continue Keep the alias", "")]
    [InlineData(JobStatus.Checking, JobStatus.NeedsHelp, "Sent", "continue Keep the alias", "")]
    [InlineData(JobStatus.AwaitingReview, JobStatus.NeedsHelp, "Sent", "continue Keep the alias", "")]
    [InlineData(JobStatus.Running, JobStatus.AwaitingReview, "Queued", "", "Keep the alias")]
    [InlineData(JobStatus.Checking, JobStatus.AwaitingReview, "Queued", "", "Keep the alias")]
    [InlineData(JobStatus.NeedsHelp, JobStatus.Running, "Queued", "", "Keep the alias")]
    [InlineData(JobStatus.AwaitingReview, JobStatus.Running, "Queued", "", "Keep the alias")]
    public async Task AMessageSentFromAScreenBehindTheJobFollowsTheJobsRealState(JobStatus shown, JobStatus real, string delivery, string call, string queued)
    {
        jobs.Status = real;

        var sent = Outcomes.Succeeds(await steering.SendAsync(job, shown, "Keep the alias", Cancellation));

        Assert.Equal(Enum.Parse<MessageDelivery>(delivery), sent);
        Assert.Equal(call.Length == 0 ? [] : [call], jobs.Calls);
        Assert.Equal(queued, steering.Queued(job).Match(message => message, () => string.Empty));
    }

    [Fact]
    public async Task AMessageQueuedBecauseTheTurnHadEndedContinuesTheJobWhenItNextNeedsYou()
    {
        jobs.Status = JobStatus.Checking;
        Assert.Equal(MessageDelivery.Queued, Outcomes.Succeeds(await steering.SendAsync(job, JobStatus.Running, "Keep the alias", Cancellation)));

        jobs.Status = JobStatus.NeedsHelp;
        await queue.HandleAsync(new JobProgressed(job, JobStatus.NeedsHelp), Cancellation);

        Assert.Equal(["continue Keep the alias"], jobs.Calls);
        Assert.True(steering.Queued(job).IsNone);
    }

    [Fact]
    public async Task InterruptingAndStoppingBothHoldTheJobWithTheirOwnReasonAndNeitherDiscardsIt()
    {
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

    public ValueTask DisposeAsync() => queue.DisposeAsync();
}
