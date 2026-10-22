using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Tests.Coordination;
using Avala.Testing;

namespace Avala.Jobs.Tests.Holding;

public sealed class IdleSessionsTests
{
    private static readonly ResumeToken Token = new("conversation-1");

    private static readonly TimeSpan Cache = TimeSpan.FromMinutes(5);

    private static CancellationToken Cancellation => JobFlow.Cancellation;

    [Fact]
    public async Task AHeldJobsResumableSessionIdlePastItsPromptCacheIsRestedAsync()
    {
        var flow = JobFlow.With();
        var (job, session) = await IdleAsync(flow, CapabilitySet.Of(new Resumable(), new CachesPrompt(Cache)));

        await flow.Idle.HandleAsync(new JobProgressed(job.Id, JobStatus.NeedsHelp), Cancellation);
        flow.IdleClock.Advance(Cache);

        Assert.Equal(new JobSessionRested(job.Id, session), await flow.Bus.WaitForAsync<JobSessionRested>(_ => true, Cancellation));
        Assert.Equal([session], flow.Agents.Stopped);
    }

    [Fact]
    public async Task AResumableSessionThatDeclaresNoPromptCacheIsRestedAfterAnHourAsync()
    {
        var flow = JobFlow.With();
        var (job, session) = await IdleAsync(flow, CapabilitySet.Of(new Resumable()));

        await flow.Idle.HandleAsync(new JobProgressed(job.Id, JobStatus.NeedsHelp), Cancellation);
        flow.IdleClock.Advance(Cache);
        await flow.SettledAsync(job);
        var early = flow.Agents.Stopped;
        flow.IdleClock.Advance(Avala.Jobs.Holding.IdleSessions.UndeclaredCache);

        Assert.Equal(new JobSessionRested(job.Id, session), await flow.Bus.WaitForAsync<JobSessionRested>(_ => true, Cancellation));
        Assert.Empty(early);
    }

    [Fact]
    public async Task AHeldJobsSessionIsRestedOnceItsTurnFinishesAndItsPromptCacheExpiresAsync()
    {
        var flow = JobFlow.With();
        var (job, session) = await IdleAsync(flow, CapabilitySet.Of(new Resumable(), new CachesPrompt(Cache)));

        await flow.Idle.HandleAsync(new JobSessionStarted(job.Id, session), Cancellation);
        await flow.Idle.HandleAsync(new TurnFinished(session, TurnId.New(), TurnOutcome.Finished), Cancellation);
        flow.IdleClock.Advance(Cache);

        Assert.Equal(new JobSessionRested(job.Id, session), await flow.Bus.WaitForAsync<JobSessionRested>(_ => true, Cancellation));
    }

    [Fact]
    public async Task TurningRemoteControlOnKeepsAnIdleSessionOpenPastItsPromptCacheAsync()
    {
        var flow = JobFlow.With();
        var (job, _) = await IdleAsync(flow, CapabilitySet.Of(new Resumable(), new CachesPrompt(Cache)));

        await flow.Idle.HandleAsync(new JobProgressed(job.Id, JobStatus.NeedsHelp), Cancellation);
        await flow.Idle.HandleAsync(new JobRemoteControlChosen(job.Id, On: true), Cancellation);
        flow.IdleClock.Advance(Cache);
        await flow.SettledAsync(job);

        Assert.Empty(flow.Agents.Stopped);
    }

    [Fact]
    public async Task AStoppedSessionIsNeverRestedAsync()
    {
        var flow = JobFlow.With();
        var (job, session) = await IdleAsync(flow, CapabilitySet.Of(new Resumable(), new CachesPrompt(Cache)));

        await flow.Idle.HandleAsync(new SessionStopped(session), Cancellation);
        await flow.Idle.HandleAsync(new JobProgressed(job.Id, JobStatus.NeedsHelp), Cancellation);
        flow.IdleClock.Advance(Avala.Jobs.Holding.IdleSessions.UndeclaredCache);
        await flow.SettledAsync(job);

        Assert.Empty(flow.Agents.Stopped);
    }

    [Fact]
    public async Task AnEndedSessionIsNeverRestedAsync()
    {
        var flow = JobFlow.With();
        var (job, session) = await IdleAsync(flow, CapabilitySet.Of(new Resumable(), new CachesPrompt(Cache)));

        await flow.Idle.HandleAsync(new SessionEnded(session, SessionEnding.Crashed), Cancellation);
        await flow.Idle.HandleAsync(new JobProgressed(job.Id, JobStatus.NeedsHelp), Cancellation);
        flow.IdleClock.Advance(Avala.Jobs.Holding.IdleSessions.UndeclaredCache);
        await flow.SettledAsync(job);

        Assert.Empty(flow.Agents.Stopped);
    }

    [Fact]
    public async Task AJobHeldNearItsUsageLimitIsNotRestedWhenItsPromptCacheExpiresAsync()
    {
        var flow = JobFlow.With();
        var (job, _) = await IdleAsync(flow, CapabilitySet.Of(new Resumable(), new CachesPrompt(Cache)), HoldReason.LimitNearlyReached);

        await flow.Idle.HandleAsync(new JobProgressed(job.Id, JobStatus.NeedsHelp), Cancellation);
        flow.IdleClock.Advance(Cache);
        await flow.SettledAsync(job);

        Assert.Empty(flow.Agents.Stopped);
    }

    [Fact]
    public async Task RestingAHeldJobOnRequestStopsItsIdleSessionAsync()
    {
        var flow = JobFlow.With();
        var (job, session) = await IdleAsync(flow, CapabilitySet.Of(new Resumable()));

        Assert.Equal(job.Id, Outcomes.Succeeds(await flow.Idle.RestAsync(job.Id, Cancellation)));
        Assert.Equal([session], flow.Agents.Stopped);
    }

    [Fact]
    public async Task RestingARunningJobIsRefusedAsNotHeldAndAnUnknownOneAsUnknownAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.RunningAsync();

        Assert.Equal(JobRejection.NotHeld, Outcomes.FailsWith(await flow.Idle.RestAsync(job.Id, Cancellation)));
        Assert.Equal(JobRejection.UnknownJob, Outcomes.FailsWith(await flow.Idle.RestAsync(JobId.New(), Cancellation)));
        Assert.Empty(flow.Agents.Stopped);
    }

    private static async Task<(Job Job, SessionId Session)> IdleAsync(JobFlow flow, CapabilitySet capabilities, HoldReason reason = HoldReason.Stalled)
    {
        var job = await flow.HeldAsync(reason, Token);
        var session = Outcomes.Present(job.Session);
        await flow.Idle.HandleAsync(new SessionOpened(session, new ProviderInfo("scripted", "Scripted"), "/repos/shop", new ConnectionName("default")) { Capabilities = capabilities }, Cancellation);

        return (job, session);
    }
}
