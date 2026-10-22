using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Steering;

namespace Avala.Workbench.Tests.Steering;

public sealed class JobSteeringTests : IAsyncDisposable
{
    private readonly FakeJobs jobs = new();
    private readonly QueuedMessages queue;
    private readonly JobSteering steering;
    private readonly JobId job = JobId.New();
    private readonly Avala.Workbench.Board.JobBoard board = new();
    private readonly Avala.Workbench.Drafting.DraftBook drafts;
    private readonly CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger messenger = new();

    public JobSteeringTests()
    {
        drafts = new Avala.Workbench.Drafting.DraftBook(new FakeDraftStore(), board, TimeProvider.System);
        queue = new QueuedMessages(jobs, drafts, messenger);
        steering = new JobSteering(jobs, queue);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(JobStatus.NeedsHelp, "continue Use the staging database")]
    [InlineData(JobStatus.AwaitingReview, "send back Use the staging database")]
    [InlineData(JobStatus.Approved, "continue Use the staging database")]
    public async Task AMessageContinuesAJobThatNeedsYouOrWasApprovedAndSendsBackAJobAwaitingReview(JobStatus status, string call)
    {
        jobs.Status = status;

        Outcomes.Succeeds(await steering.SendAsync(job, status, "Use the staging database", Cancellation));

        Assert.Equal([call], jobs.Calls);
    }

    [Theory]
    [InlineData(JobStatus.Discarded)]
    [InlineData(JobStatus.Failed)]
    public async Task AMessageToADiscardedOrFailedJobIsRefusedWithoutAskingTheJob(JobStatus status)
    {
        Assert.Equal(JobRejection.NotHeld, Outcomes.FailsWith(await steering.SendAsync(job, status, "Hurry", Cancellation)));
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
    public async Task APersonsMessageQueuedWithAvalasNoticeContinuesTheJobAloneAsThePersonsAndTheNoticeFollowsAsAvalasOwn()
    {
        const string Notice = "Avala now allows reaching https://example.com in this job. You may retry it.";
        jobs.Status = JobStatus.Checking;
        _ = Outcomes.Succeeds(await steering.SendAsync(job, JobStatus.Running, new JobMessage(Notice) { Author = MessageAuthor.Avala, CausedBy = MessageAuthor.Person }, Cancellation));
        _ = Outcomes.Succeeds(await steering.SendAsync(job, JobStatus.Running, new JobMessage("Keep the old alias."), Cancellation));

        jobs.Status = JobStatus.NeedsHelp;
        await queue.HandleAsync(new JobProgressed(job, JobStatus.NeedsHelp), Cancellation);

        Assert.Equal(
            [("Keep the old alias.", MessageAuthor.Person, MessageAuthor.Person), (Notice, MessageAuthor.Avala, MessageAuthor.Person)],
            jobs.Messages.Skip(jobs.Messages.Count - 2).Select(message => (message.Text, message.Author, message.CausedBy)));
    }

    [Fact]
    public async Task WithdrawingWhatThePersonQueuedKeepsAvalasNoticeQueuedAndOutOfThePersonsText()
    {
        const string Notice = "Avala now allows reaching https://example.com in this job. You may retry it.";
        jobs.Status = JobStatus.Checking;
        _ = Outcomes.Succeeds(await steering.SendAsync(job, JobStatus.Running, new JobMessage(Notice) { Author = MessageAuthor.Avala }, Cancellation));
        _ = Outcomes.Succeeds(await steering.SendAsync(job, JobStatus.Running, new JobMessage("Keep the old alias."), Cancellation));
        var queued = steering.Queued(job);

        Assert.True(steering.Withdraw(job));
        jobs.Status = JobStatus.NeedsHelp;
        await queue.HandleAsync(new JobProgressed(job, JobStatus.NeedsHelp), Cancellation);

        Assert.Equal((Option<string>.Some("Keep the old alias."), Option<string>.None), (queued, steering.Queued(job)));
        Assert.Equal((Notice, MessageAuthor.Avala), (jobs.Messages[^1].Text, jobs.Messages[^1].Author));
    }

    [Fact]
    public async Task TwoOfAvalasNoticesQueuedForTheSameJobStillReachItAsAvalasOwn()
    {
        jobs.Status = JobStatus.Checking;
        _ = Outcomes.Succeeds(await steering.SendAsync(job, JobStatus.Running, new JobMessage("Avala now allows reaching https://example.com in this job. You may retry it.") { Author = MessageAuthor.Avala }, Cancellation));
        _ = Outcomes.Succeeds(await steering.SendAsync(job, JobStatus.Running, new JobMessage("Avala now allows running dotnet test in this job. You may retry it.") { Author = MessageAuthor.Avala }, Cancellation));

        jobs.Status = JobStatus.NeedsHelp;
        await queue.HandleAsync(new JobProgressed(job, JobStatus.NeedsHelp), Cancellation);

        Assert.Equal(MessageAuthor.Avala, jobs.Messages[^1].Author);
    }

    [Fact]
    public async Task AMessageToAnApprovedJobThatAWakeUpAlreadyReopenedJoinsItsRunningTurn()
    {
        (jobs.Status, jobs.Steerable) = (JobStatus.Running, true);

        Assert.Equal(MessageDelivery.JoinedTheTurn, Outcomes.Succeeds(await steering.SendAsync(job, JobStatus.Approved, "Keep the alias", Cancellation)));
        Assert.Equal(["steer Keep the alias"], jobs.Calls);
    }

    [Fact]
    public async Task AMessageWaitingWhenTheJobIsApprovedAgainReopensItAndOneWaitingWhenItIsDiscardedComesBackUndelivered()
    {
        var discarded = JobId.New();
        jobs.Status = JobStatus.Checking;
        Assert.Equal(MessageDelivery.Queued, Outcomes.Succeeds(await steering.SendAsync(job, JobStatus.Running, "Keep the alias", Cancellation)));
        Assert.Equal(MessageDelivery.Queued, Outcomes.Succeeds(await steering.SendAsync(discarded, JobStatus.Running, "Drop it", Cancellation)));

        jobs.Status = JobStatus.Approved;
        await queue.HandleAsync(new JobProgressed(job, JobStatus.Approved), Cancellation);
        await queue.HandleAsync(new JobProgressed(discarded, JobStatus.Discarded), Cancellation);

        Assert.Equal(["continue Keep the alias"], jobs.Calls);
        Assert.Equal((true, true), (steering.Queued(job).IsNone, steering.Queued(discarded).IsNone));
        Assert.Equal([("Drop it", JobStatus.Discarded, Option<JobRejection>.None)], Back(discarded));
    }

    [Theory]
    [InlineData(JobStatus.Discarded)]
    [InlineData(JobStatus.Failed)]
    public async Task AMessageSentFromAScreenThatStillShowsTheJobRunningAfterItEndedIsNotLeftQueuedForAJobThatWillNeverTakeIt(JobStatus ended)
    {
        jobs.Status = ended;
        await queue.HandleAsync(new JobProgressed(job, ended), Cancellation);

        var sent = await steering.SendAsync(job, JobStatus.Running, "Keep the alias", Cancellation);

        Assert.Equal((false, true), (sent.Match(delivery => delivery == MessageDelivery.Queued, _ => false), steering.Queued(job).IsNone));
    }

    [Fact]
    public async Task AQueuedMessageThatComesBackIsAnnouncedAsQueuedAndThenReturned()
    {
        List<Avala.Workbench.Contracts.Presentation.QueueChange> changes = [];
        CommunityToolkit.Mvvm.Messaging.IMessengerExtensions.Register<Avala.Workbench.Contracts.Presentation.QueuedMessageChanged>(messenger, this, (_, changed) => changes.Add(changed.Change));
        jobs.Status = JobStatus.Checking;
        Assert.Equal(MessageDelivery.Queued, Outcomes.Succeeds(await steering.SendAsync(job, JobStatus.Running, "Keep the alias", Cancellation)));

        await queue.HandleAsync(new JobProgressed(job, JobStatus.Discarded), Cancellation);

        Assert.Equal([Avala.Workbench.Contracts.Presentation.QueueChange.Queued, Avala.Workbench.Contracts.Presentation.QueueChange.Returned], changes);
    }

    [Fact]
    public async Task AMessageThatComesBackUndeliveredRefreshesWhatThePersonSees()
    {
        jobs.Status = JobStatus.Checking;
        Assert.Equal(MessageDelivery.Queued, Outcomes.Succeeds(await steering.SendAsync(job, JobStatus.Running, "Keep the alias", Cancellation)));
        using var watching = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        await using var changes = board.ChangesAsync(watching.Token).GetAsyncEnumerator(Cancellation);
        Assert.True(await changes.MoveNextAsync());

        await queue.HandleAsync(new JobProgressed(job, JobStatus.Discarded), Cancellation);

        Assert.True(await changes.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), Cancellation));
        await watching.CancelAsync();
    }

    [Fact]
    public async Task AMessageWaitingForAHeldJobThatCannotContinueYetStaysQueued()
    {
        jobs.Status = JobStatus.Checking;
        Assert.Equal(MessageDelivery.Queued, Outcomes.Succeeds(await steering.SendAsync(job, JobStatus.Running, "Keep the alias", Cancellation)));

        (jobs.Status, jobs.Refusal) = (JobStatus.NeedsHelp, JobRejection.WorkspaceUnavailable);
        await queue.HandleAsync(new JobProgressed(job, JobStatus.NeedsHelp), Cancellation);

        Assert.Equal(Option<string>.Some("Keep the alias"), steering.Queued(job));
    }

    [Fact]
    public async Task AMessageWaitingForAJobApprovedWithoutItsWorktreeComesBackUndeliveredSayingWhy()
    {
        jobs.Status = JobStatus.Checking;
        Assert.Equal(MessageDelivery.Queued, Outcomes.Succeeds(await steering.SendAsync(job, JobStatus.Running, "Keep the alias", Cancellation)));

        (jobs.Status, jobs.Refusal) = (JobStatus.Approved, JobRejection.WorkspaceUnavailable);
        await queue.HandleAsync(new JobProgressed(job, JobStatus.Approved), Cancellation);

        Assert.True(steering.Queued(job).IsNone);
        Assert.Equal([("Keep the alias", JobStatus.Approved, Option<JobRejection>.Some(JobRejection.WorkspaceUnavailable))], Back(job));
    }

    [Fact]
    public async Task AMessageToAnIntegratedChildIsRefusedAndOneWaitingWhenItIsIntegratedComesBackUndeliveredSayingWhy()
    {
        jobs.Status = JobStatus.Checking;
        Assert.Equal(MessageDelivery.Queued, Outcomes.Succeeds(await steering.SendAsync(job, JobStatus.Running, "Keep the alias", Cancellation)));

        (jobs.Status, jobs.Refusal) = (JobStatus.Approved, JobRejection.NotRoot);
        await queue.HandleAsync(new JobProgressed(job, JobStatus.Approved), Cancellation);

        Assert.Equal(JobRejection.NotRoot, Outcomes.FailsWith(await steering.SendAsync(job, JobStatus.Approved, "And another", Cancellation)));
        Assert.True(steering.Queued(job).IsNone);
        Assert.Equal([("Keep the alias", JobStatus.Approved, Option<JobRejection>.Some(JobRejection.NotRoot))], Back(job));
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
    [InlineData("Approved", true, false, false, false, false)]
    [InlineData("Discarded", false, false, false, false, false)]
    public void WhatAJobAcceptsDependsOnItsStatus(string status, bool messages, bool interrupt, bool stop, bool review, bool discard)
    {
        var parsed = Enum.Parse<JobStatus>(status);

        Assert.Equal(
            (messages, interrupt, stop, review, discard),
            (parsed.AcceptsMessages, parsed.CanBeInterrupted, parsed.CanBeStopped, parsed.CanBeReviewed, parsed.CanBeDiscarded));
    }

    public ValueTask DisposeAsync() => queue.DisposeAsync();

    private (string Text, JobStatus Status, Option<JobRejection> Rejection)[] Back(JobId of) =>
        steering.TakeBack(of).Match<(string, JobStatus, Option<JobRejection>)[]>(back => [(drafts.Of(of).Match(draft => draft.Text, () => string.Empty), back.Status, back.Rejection)], () => []);
}
