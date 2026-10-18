using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;
using Avala.Sdk.Processes;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class IdleSessionTests(PublishedPlugins plugins)
{
    private static readonly TimeSpan PromptCache = TimeSpan.FromHours(1);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobWaitingForReviewEndsItsIdleAgentOnceItsPromptCacheExpiresAndAMessageResumesItsConversationAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "reply");
        var stopped = run.Watch<SessionStopped>();
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await run.DeliveredAsync();
        var session = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(run.Job, Cancellation)).Sessions[^1].Session;

        run.Clock.Advance(PromptCache);
        _ = await stopped.UntilAsync(found => found.Session == session);
        await run.DeliveredAsync();

        Assert.Empty(run.Get<IProcessTrees>().Open);
        var continued = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(run.Job, "Greet the new hires too.", Cancellation));
        Assert.Equal(ContinuedIn.ResumedConversation, continued.Conversation);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
    }

    [Fact]
    public async Task AnAgentKeptForRemoteControlEndsOnceRemoteControlIsTurnedOffPastItsPromptCacheAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, RemoteLogin, []);
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("reply"))));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        Outcomes.Succeeds(await run.Get<IRemoteJobs>().ControlRemotelyAsync(job, true, Cancellation));
        await run.DeliveredAsync();
        run.Clock.Advance(PromptCache);
        await run.DeliveredAsync();
        var session = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Sessions[^1].Session;
        Assert.True(run.Get<IAgents>().IsOpen(session));
        var stopped = run.Watch<SessionStopped>();

        Outcomes.Succeeds(await run.Get<IRemoteJobs>().ControlRemotelyAsync(job, false, Cancellation));
        await run.DeliveredAsync();
        run.Clock.Advance(PromptCache);
        _ = await stopped.UntilAsync(found => found.Session == session);
        await run.DeliveredAsync();

        Assert.Empty(run.Get<IProcessTrees>().Open);
    }

    private static readonly (string File, string Content)[] RemoteLogin =
    [
        ("connections.json", """{ "default": "claude", "connections": [ { "name": "claude", "provider": "simulator", "credential": { "source": "login" } } ] }"""),
        ("connections/claude/.login", string.Empty),
    ];

    [Fact]
    public async Task ArchivingAConversationWaitingForReviewEndsItsAgentAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "reply");
        var stopped = run.Watch<SessionStopped>();
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await run.DeliveredAsync();

        _ = Outcomes.Succeeds(await run.Get<IProjects>().ArchiveAsync([run.Job], Cancellation));
        _ = await stopped.UntilAsync(_ => true);
        await run.DeliveredAsync();

        Assert.Empty(run.Get<IProcessTrees>().Open);
    }

    [Fact]
    public async Task AnAgentWaitingForAPersonsAnswerIsKeptPastItsPromptCacheAndTheAnswerReachesItAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "waiting-permission");
        var asked = await run.DecisionAsync();
        await run.DeliveredAsync();

        run.Clock.Advance(PromptCache + PromptCache);
        await run.DeliveredAsync();

        Assert.True(run.Get<IAgents>().IsOpen(asked.Session));
        Outcomes.Succeeds(await run.Get<IAgents>().RespondAsync(asked.Session, new PermissionDecision(asked.Item, PermissionAnswer.Allow), Cancellation));
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
    }
}
