using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Jobs.Tests.Coordination;

public sealed class ConnectionTests
{
    private static readonly ConnectionName Work = new("work");

    private static readonly ConnectionName Personal = new("personal");

    private static CancellationToken Cancellation => JobFlow.Cancellation;

    [Theory]
    [InlineData("UnknownConnection", "UnknownConnection")]
    [InlineData("MissingFolder", "UnusableConnection")]
    [InlineData("MissingVariable", "UnusableConnection")]
    public async Task AJobNamingAConnectionThatCannotBeUsedIsRejectedAtSubmissionAsync(string refusal, string rejection)
    {
        var flow = JobFlow.With();
        flow.Connections.Refused[Work.Value] = Enum.Parse<ConnectionError>(refusal);

        var submitted = await flow.Jobs.SubmitAsync(JobFlow.Request() with { Connection = Work }, Cancellation);

        Assert.Equal(Enum.Parse<JobRejection>(rejection), Outcomes.FailsWith(submitted));
        Assert.Empty(flow.Store.Jobs);
        Assert.Empty(flow.Bus.Published);
    }

    [Fact]
    public async Task AJobNamingAConnectionRunsOnItWhateverItsRepositoryPrefersAsync()
    {
        var flow = JobFlow.With();
        flow.Defaults.Connection = Option<ConnectionName>.Some(Personal);

        var job = await flow.RunningAsync(JobFlow.Request() with { Connection = Work });

        Assert.Equal(Option<ConnectionName>.Some(Work), Assert.Single(flow.Agents.Requests).Connection);
        Assert.Equal((JobState.Running, Option<ConnectionName>.Some(Work)), (job.State, job.Connection));
        Assert.Empty(flow.Defaults.Read);
    }

    [Fact]
    public async Task AJobWithoutAConnectionRunsOnItsRepositorysDefaultAsync()
    {
        var flow = JobFlow.With();
        flow.Defaults.Connection = Option<ConnectionName>.Some(Personal);

        var job = await flow.RunningAsync();

        Assert.Equal(Option<ConnectionName>.Some(Personal), Assert.Single(flow.Agents.Requests).Connection);
        Assert.Equal([Assert.Single(flow.Agents.Sessions).Value], flow.Defaults.Read);
        Assert.Equal(Option<ConnectionName>.Some(Personal), job.Connection);
    }

    [Fact]
    public async Task AJobWithoutAnyPreferenceKeepsTheDefaultConnectionItsSessionOpenedOnAsync()
    {
        var flow = JobFlow.With();

        var job = await flow.RunningAsync();

        Assert.True(Assert.Single(flow.Agents.Requests).Connection.IsNone);
        Assert.Equal(Option<ConnectionName>.Some(FakeAgents.DefaultConnection), job.Connection);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ARepositoryDefaultThatCannotBeUsedFailsTheJobAsync(bool fileRejected)
    {
        var flow = JobFlow.With();
        flow.Agents.UnknownConnections.Add(Personal.Value);
        flow.Defaults.Connection = fileRejected
            ? Result<Option<ConnectionName>, JobRejection>.Failure(JobRejection.UnusableConnection)
            : Option<ConnectionName>.Some(Personal);

        var job = await flow.RunningAsync();

        Assert.Equal(JobState.Failed, job.State);
        Assert.Empty(flow.Agents.Sessions);
        Assert.Equal(fileRejected ? 0 : 1, flow.Agents.Requests.Count);
    }

    [Fact]
    public async Task RecoveryOpensTheNewSessionOnTheJobsConnectionAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.RunningAsync(JobFlow.Request() with { Connection = Work });

        await flow.Recovery.RunAsync(Cancellation);

        Assert.Equal([Option<ConnectionName>.Some(Work), Option<ConnectionName>.Some(Work)], flow.Agents.Requests.Select(request => request.Connection));
        Assert.Equal((JobState.Running, Option<ConnectionName>.Some(Work)), (job.State, job.Connection));
    }

    [Fact]
    public async Task ContinuingAJobInANewSessionOpensItOnTheJobsConnectionAsync()
    {
        var flow = JobFlow.With();
        flow.Defaults.Connection = Option<ConnectionName>.Some(Personal);
        var job = await flow.HeldAsync(HoldReason.SessionLost);
        flow.Defaults.Connection = Option<ConnectionName>.Some(Work);

        Outcomes.Succeeds(await flow.Jobs.ContinueAsync(job.Id, "Go on", Cancellation));

        Assert.Equal(Option<ConnectionName>.Some(Personal), flow.Agents.Requests[^1].Connection);
    }

    [Fact]
    public async Task AJobWhoseConnectionIsGoneStaysHeldWhenContinuedAndFailsWhenRecoveredAsync()
    {
        var flow = JobFlow.With();
        var held = await flow.HeldAsync(HoldReason.SessionLost);
        var running = await flow.RunningAsync();
        flow.Agents.UnknownConnections.Add(FakeAgents.DefaultConnection.Value);

        var continued = await flow.Jobs.ContinueAsync(held.Id, "Go on", Cancellation);
        await flow.Recovery.RunAsync(Cancellation);

        Assert.Equal(JobRejection.UnknownConnection, Outcomes.FailsWith(continued));
        Assert.Equal((JobState.NeedsHelp, JobState.Failed), (held.State, running.State));
    }
}
