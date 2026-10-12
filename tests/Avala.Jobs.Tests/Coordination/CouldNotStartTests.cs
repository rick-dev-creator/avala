using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Launching;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.Logging;
using HoldAnnouncement = Avala.Jobs.Contracts.JobHeld;

namespace Avala.Jobs.Tests.Coordination;

public sealed class CouldNotStartTests
{
    private const string Locked = "The process cannot access the file because it is being used by another process.";

    private static CancellationToken Cancellation => JobFlow.Cancellation;

    [Fact]
    public async Task ALaunchStepThatThrowsHoldsTheJobAsCouldNotStartWithItsMessageAndLogsTheExceptionAsync()
    {
        var flow = JobFlow.With();
        var fault = new IOException(Locked);
        flow.Defaults.Fault = fault;

        var job = await flow.RunningAsync();

        Assert.Equal(
            (JobState.NeedsHelp, Option<HoldReason>.Some(HoldReason.CouldNotStart), Option<string>.Some(Locked)),
            (job.State, job.Held, job.Problem));
        var hold = Assert.Single(flow.Bus.Published.OfType<HoldAnnouncement>()).Hold;
        Assert.Equal(
            (job.Id, Option<SessionId>.None, HoldReason.CouldNotStart, Option<string>.Some(Locked), 0),
            (hold.Job, hold.Session, hold.Reason, hold.Problem, hold.Attempt));
        Assert.Contains(flow.Logs.Entries, entry => entry.Level == LogLevel.Error && entry.Exception == fault);
        Assert.Empty(flow.Agents.Sessions);
    }

    [Fact]
    public async Task ALaunchStepThatThrowsAfterTheSessionOpenedStopsThatSessionAsync()
    {
        var flow = JobFlow.With();
        flow.Briefings.Add(new ThrowingBriefing());

        var job = await flow.RunningAsync();

        var session = Assert.Single(flow.Agents.Stopped);
        var hold = Assert.Single(flow.Bus.Published.OfType<HoldAnnouncement>()).Hold;
        Assert.Equal(
            (JobState.NeedsHelp, Option<SessionId>.Some(session), SessionHalt.Stopped, 1),
            (job.State, hold.Session, hold.Halt, hold.Attempt));
        Assert.Empty(flow.Agents.Sessions);
        Assert.Empty(flow.Agents.Sent);
    }

    [Fact]
    public async Task RetryingAJobThatCouldNotStartOnceTheCauseIsGoneRemovesItsHalfMadeWorktreeAndRunsItAsync()
    {
        var flow = JobFlow.With();
        flow.Defaults.Fault = new IOException(Locked);
        var job = await flow.RunningAsync();
        var leftover = Outcomes.Present(job.Workspace);
        flow.Defaults.Fault = null;

        Assert.Equal(job.Id, Outcomes.Succeeds(await flow.Launches.RetryAsync(job.Id, Cancellation)));

        Assert.Equal((JobState.Running, Option<HoldReason>.None, Option<string>.None), (job.State, job.Held, job.Problem));
        Assert.True((await flow.Workspaces.FindAsync(leftover, Cancellation)).IsFailure);
        Assert.True((await flow.Workspaces.FindAsync(Outcomes.Present(job.Workspace), Cancellation)).IsSuccess);
        Assert.Equal([(Outcomes.Present(job.Session), "Add GitHub login")], flow.Agents.Sent);
    }

    [Fact]
    public async Task ARetryWhoseHalfMadeWorktreeCannotBeRemovedHoldsTheJobAgainAsync()
    {
        var flow = JobFlow.With();
        flow.Defaults.Fault = new IOException(Locked);
        var job = await flow.RunningAsync();
        var leftover = job.Workspace;
        flow.Defaults.Fault = null;
        flow.Workspaces.RemovalFails = true;

        _ = Outcomes.Succeeds(await flow.Launches.RetryAsync(job.Id, Cancellation));

        Assert.Equal(
            (JobState.NeedsHelp, Option<string>.Some(JobLauncher.LeftoverKept), leftover),
            (job.State, job.Problem, job.Workspace));
        Assert.Empty(flow.Agents.Sessions);
    }

    [Fact]
    public async Task GitFailingWhilePreparingTheWorktreeHoldsTheJobInsteadOfFailingItAsync()
    {
        var flow = new JobFlow(new FakeWorkspaces { PreparationFailure = WorkspaceFailure.GitFailed }, new FakeAgents());

        var job = await flow.RunningAsync();

        Assert.Equal(
            (JobState.NeedsHelp, Option<HoldReason>.Some(HoldReason.CouldNotStart), Option<string>.Some(JobLauncher.GitCouldNotPrepare)),
            (job.State, job.Held, job.Problem));
    }

    [Fact]
    public async Task AJobThatCouldNotStartTakesNoMessageAndOnlyItsRetryStartsItAsync()
    {
        var flow = JobFlow.With();
        flow.Defaults.Fault = new IOException(Locked);
        var job = await flow.RunningAsync();
        flow.Defaults.Fault = null;
        var stalled = await flow.HeldAsync(HoldReason.Stalled);

        Assert.Equal(JobRejection.NeverStarted, Outcomes.FailsWith(await flow.Jobs.ContinueAsync(job.Id, "Try again", Cancellation)));
        Assert.Equal(JobRejection.NotHeld, Outcomes.FailsWith(await flow.Launches.RetryAsync(stalled.Id, Cancellation)));
        Assert.Equal(JobRejection.UnknownJob, Outcomes.FailsWith(await flow.Launches.RetryAsync(JobId.New(), Cancellation)));
        Assert.Equal(JobState.NeedsHelp, job.State);
    }

    [Fact]
    public async Task AJobThatCouldNotStartIsLeftHeldByRecoveryAsync()
    {
        var flow = JobFlow.With();
        flow.Defaults.Fault = new IOException(Locked);
        var job = await flow.RunningAsync();
        flow.Defaults.Fault = null;

        await flow.Recovery.RunAsync(Cancellation);
        await flow.SettledAsync(job);

        var summary = Outcomes.Present(await flow.Catalog.HistoryAsync(job.Id, Cancellation)).Summary;
        Assert.Equal(
            (JobStatus.NeedsHelp, Option<HoldReason>.Some(HoldReason.CouldNotStart), Option<string>.Some(Locked)),
            (summary.Status, summary.Hold, summary.Problem));
        Assert.Empty(flow.Agents.Sessions);
    }

    private sealed class ThrowingBriefing : IJobBriefing
    {
        public ValueTask<Option<string>> BriefAsync(JobId job, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(Locked);
    }
}
