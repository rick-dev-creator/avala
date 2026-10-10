using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Tests.Coordination;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;
using JobApproved = Avala.Jobs.Contracts.JobApproved;

namespace Avala.Jobs.Tests.Review;

public sealed class ReviewJobTests
{
    private const string Feedback = "Keep the old login working too";

    private static readonly ResumeToken Token = new("conversation-1");

    private static CancellationToken Cancellation => JobFlow.Cancellation;

    [Fact]
    public async Task ApprovingWithoutADeclaredStrategyKeepsTheBranchAndEndsTheJobAsync()
    {
        var flow = JobFlow.With();
        var job = await ReviewedAsync(flow);
        var session = Outcomes.Present(job.Session);
        var branch = Outcomes.Succeeds(await flow.Workspaces.FindAsync(Outcomes.Present(job.Workspace), Cancellation)).Branch;

        var approval = Outcomes.Succeeds(await flow.Jobs.ApproveAsync(job.Id, Cancellation));

        Assert.Equal(new JobApproval(job.Id, new ApprovalDelivery("keep", branch, Option<string>.None)), approval);
        Assert.Equal(JobState.Approved, job.State);
        Assert.Equal([session], flow.Agents.Stopped);
        Assert.Equal([new JobProgressed(job.Id, JobStatus.Approved), new JobApproved(approval)], flow.Bus.Published.TakeLast(2));
    }

    [Fact]
    public async Task ApprovingDeliversThroughTheStrategyTheRepositoryNamesAsync()
    {
        var flow = JobFlow.With();
        var strategy = new ScriptedStrategy("merge", new ApprovalDelivery("merge", "main", "abc123"));
        flow.Strategies.Add(strategy);
        flow.Defaults.Approval = Option<string>.Some("merge");
        var job = await ReviewedAsync(flow);

        var approval = Outcomes.Succeeds(await flow.Jobs.ApproveAsync(job.Id, Cancellation));

        var request = Assert.Single(strategy.Requests);
        Assert.Equal((job.Id, "Add GitHub login", 1, job.Workspace), (request.Job, request.Instruction, request.Attempts, Option<WorkspaceId>.Some(request.Workspace.Id)));
        Assert.Equal(new ApprovalDelivery("merge", "main", "abc123"), approval.Delivery);
        Assert.Equal(JobState.Approved, job.State);
    }

    [Theory]
    [InlineData("MergeConflict", "merge", false)]
    [InlineData("UnknownApprovalStrategy", "squash", false)]
    [InlineData("InvalidJobFile", "", true)]
    public async Task AnApprovalThatCannotBeDeliveredLeavesTheJobAwaitingReviewAsync(string expected, string declared, bool invalidFile)
    {
        var rejection = Enum.Parse<JobRejection>(expected);
        var flow = JobFlow.With();
        flow.Strategies.Add(new ScriptedStrategy("merge", rejection));
        flow.Defaults.Approval = invalidFile
            ? Result<Option<string>, JobRejection>.Failure(JobRejection.InvalidJobFile)
            : Option<string>.Some(declared);
        var job = await ReviewedAsync(flow);
        var published = flow.Bus.Published.Count;

        Assert.Equal(rejection, Outcomes.FailsWith(await flow.Jobs.ApproveAsync(job.Id, Cancellation)));

        Assert.Equal(JobState.AwaitingReview, job.State);
        Assert.Empty(flow.Agents.Stopped);
        Assert.Equal(published, flow.Bus.Published.Count);
    }

    [Fact]
    public async Task OnlyAJobAwaitingReviewCanBeApprovedAsync()
    {
        var flow = JobFlow.With();
        var running = await flow.RunningAsync();

        Assert.Equal(JobRejection.NotAwaitingReview, Outcomes.FailsWith(await flow.Jobs.ApproveAsync(running.Id, Cancellation)));
        Assert.Equal(JobRejection.UnknownJob, Outcomes.FailsWith(await flow.Jobs.ApproveAsync(JobId.New(), Cancellation)));
        Assert.Equal(JobState.Running, running.State);
    }

    [Fact]
    public async Task SendingBackAJobStartsANewRoundInItsSessionWithTheFeedbackAsync()
    {
        var flow = JobFlow.With();
        var job = await ReviewedAsync(flow);
        var session = Outcomes.Present(job.Session);

        var continued = Outcomes.Succeeds(await flow.Jobs.SendBackAsync(job.Id, Feedback, Cancellation));

        Assert.Equal(new JobContinuation(job.Id, session, ContinuedIn.SameSession), continued);
        Assert.Equal((JobState.Running, AttemptOrigin.SendBack), (job.State, job.Attempts[^1].Origin));
        Assert.Equal((session, Feedback), flow.Agents.Sent[^1]);
        Assert.Contains(new JobProgressed(job.Id, JobStatus.Running), flow.Bus.Published);
    }

    [Fact]
    public async Task SendingBackAJobWhoseSessionIsGoneResumesItsConversationInANewSessionAsync()
    {
        var flow = new JobFlow(new FakeWorkspaces(), new FakeAgents { Resumes = true });
        var job = await ReviewedAsync(flow);
        await flow.OfferResumeAsync(job, Outcomes.Present(job.Session), Token);
        _ = await flow.Agents.StopAsync(Outcomes.Present(job.Session), Cancellation);

        var continued = Outcomes.Succeeds(await flow.Jobs.SendBackAsync(job.Id, Feedback, Cancellation));

        Assert.Equal(ContinuedIn.ResumedConversation, continued.Conversation);
        Assert.Equal(Option<SessionId>.Some(continued.Session), job.Session);
        Assert.Equal(Option<ResumeToken>.Some(Token), flow.Agents.Requests[^1].Resume);
        Assert.Equal((continued.Session, Feedback), flow.Agents.Sent[^1]);
        Assert.Equal(new JobSessionStarted(job.Id, continued.Session), flow.Bus.Published.OfType<JobSessionStarted>().Last());
    }

    [Fact]
    public async Task OnlyAJobAwaitingReviewCanBeSentBackAndOnlyWithFeedbackAsync()
    {
        var flow = JobFlow.With();
        var running = await flow.RunningAsync();
        var reviewed = await ReviewedAsync(flow);
        var sent = flow.Agents.Sent.Count;

        Assert.Equal(JobRejection.NotAwaitingReview, Outcomes.FailsWith(await flow.Jobs.SendBackAsync(running.Id, Feedback, Cancellation)));
        Assert.Equal(JobRejection.EmptyMessage, Outcomes.FailsWith(await flow.Jobs.SendBackAsync(reviewed.Id, " ", Cancellation)));
        Assert.Equal(JobRejection.UnknownJob, Outcomes.FailsWith(await flow.Jobs.SendBackAsync(JobId.New(), Feedback, Cancellation)));
        Assert.Equal((JobState.Running, JobState.AwaitingReview), (running.State, reviewed.State));
        Assert.Equal(sent, flow.Agents.Sent.Count);
    }

    [Fact]
    public async Task ApprovingThroughANamedStrategyDeliversThroughItInsteadOfTheRepositorysAsync()
    {
        var flow = JobFlow.With();
        var named = new ScriptedStrategy("pull-request", new ApprovalDelivery("pull-request", "avala/job", "abc123"));
        flow.Strategies.Add(named);
        var job = await ReviewedAsync(flow);

        var approval = Outcomes.Succeeds(await flow.Jobs.ApproveThroughAsync(job.Id, "pull-request", Cancellation));

        Assert.Single(named.Requests);
        Assert.Equal(new ApprovalDelivery("pull-request", "avala/job", "abc123"), approval.Delivery);
        Assert.Equal(JobState.Approved, job.State);
        Assert.Equal(JobRejection.UnknownApprovalStrategy, Outcomes.FailsWith(await flow.Jobs.ApproveThroughAsync((await ReviewedAsync(flow)).Id, "squash", Cancellation)));
    }

    [Fact]
    public async Task ReopeningAnApprovedJobStartsASendBackRoundInANewSessionThatResumesItsConversationAsync()
    {
        var flow = new JobFlow(new FakeWorkspaces(), new FakeAgents { Resumes = true });
        var job = await ReviewedAsync(flow);
        await flow.OfferResumeAsync(job, Outcomes.Present(job.Session), Token);
        _ = Outcomes.Succeeds(await flow.Jobs.ApproveAsync(job.Id, Cancellation));

        var continued = Outcomes.Succeeds(await flow.Jobs.ReopenAsync(job.Id, Feedback, Cancellation));

        Assert.Equal(ContinuedIn.ResumedConversation, continued.Conversation);
        Assert.Equal((JobState.Running, AttemptOrigin.SendBack, Option<DateTimeOffset>.None), (job.State, job.Attempts[^1].Origin, job.Ended));
        Assert.Equal(Option<ResumeToken>.Some(Token), flow.Agents.Requests[^1].Resume);
        Assert.Equal((continued.Session, Feedback), flow.Agents.Sent[^1]);
        Assert.Equal(new JobProgressed(job.Id, JobStatus.Running), flow.Bus.Published.OfType<JobProgressed>().Last());
    }

    [Fact]
    public async Task OnlyAnApprovedJobWithItsWorktreeCanBeReopenedAndOnlyWithFeedbackAsync()
    {
        var flow = JobFlow.With();
        var reviewed = await ReviewedAsync(flow);
        var gone = await ReviewedAsync(flow);
        _ = Outcomes.Succeeds(await flow.Jobs.ApproveAsync(gone.Id, Cancellation));
        _ = await flow.Workspaces.RemoveAsync(Outcomes.Present(gone.Workspace), Cancellation);
        var sent = flow.Agents.Sent.Count;

        Assert.Equal(JobRejection.NotApproved, Outcomes.FailsWith(await flow.Jobs.ReopenAsync(reviewed.Id, Feedback, Cancellation)));
        Assert.Equal(JobRejection.EmptyMessage, Outcomes.FailsWith(await flow.Jobs.ReopenAsync(gone.Id, " ", Cancellation)));
        Assert.Equal(JobRejection.WorkspaceUnavailable, Outcomes.FailsWith(await flow.Jobs.ReopenAsync(gone.Id, Feedback, Cancellation)));
        Assert.Equal(JobRejection.UnknownJob, Outcomes.FailsWith(await flow.Jobs.ReopenAsync(JobId.New(), Feedback, Cancellation)));
        Assert.Equal((JobState.AwaitingReview, JobState.Approved), (reviewed.State, gone.State));
        Assert.Equal(sent, flow.Agents.Sent.Count);
    }

    private static async Task<Job> ReviewedAsync(JobFlow flow)
    {
        var job = await flow.RunningAsync();
        await flow.FinishTurnAsync(job);
        Assert.Equal(JobState.AwaitingReview, job.State);

        return job;
    }
}
