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
    public async Task AJobNamingAConnectionRunsOnItWhateverItsRepositoryPrefersOrCapacitySaysAsync()
    {
        var flow = JobFlow.With();
        var selector = new FakeSelector(Personal);
        flow.Selectors.Add(selector);
        flow.Connections.Declared.AddRange([Declared(Work), Declared(Personal)]);
        flow.Defaults.Connection = Option<ConnectionName>.Some(Personal);

        var job = await flow.RunningAsync(JobFlow.Request() with { Connection = Work });

        Assert.Equal(Option<ConnectionName>.Some(Work), Assert.Single(flow.Agents.Requests).Connection);
        Assert.Equal((JobState.Running, Option<ConnectionName>.Some(Work)), (job.State, job.Connection));
        Assert.Empty(flow.Defaults.Read);
        Assert.Empty(selector.Questions);
    }

    [Fact]
    public async Task AJobWithoutAConnectionRunsOnItsRepositorysDefaultWhateverCapacitySaysAsync()
    {
        var flow = JobFlow.With();
        var selector = new FakeSelector(Work);
        flow.Selectors.Add(selector);
        flow.Connections.Declared.AddRange([Declared(Work), Declared(Personal)]);
        flow.Defaults.Connection = Option<ConnectionName>.Some(Personal);

        var job = await flow.RunningAsync();

        Assert.Equal(Option<ConnectionName>.Some(Personal), Assert.Single(flow.Agents.Requests).Connection);
        Assert.Equal([Assert.Single(flow.Agents.Sessions).Value], flow.Defaults.Read);
        Assert.Equal(Option<ConnectionName>.Some(Personal), job.Connection);
        Assert.Empty(selector.Questions);
    }

    [Fact]
    public async Task AJobWithoutAnyPreferenceKeepsTheDefaultConnectionItsSessionOpenedOnWhenNothingSelectsByCapacityAsync()
    {
        var flow = JobFlow.With();
        flow.Connections.Declared.AddRange([Declared(Work), Declared(Personal)]);

        var job = await flow.RunningAsync();

        Assert.True(Assert.Single(flow.Agents.Requests).Connection.IsNone);
        Assert.Equal(Option<ConnectionName>.Some(FakeAgents.DefaultConnection), job.Connection);
        Assert.Empty(flow.Bus.Published.OfType<ConnectionChosen>());
    }

    [Fact]
    public async Task AJobWithoutAnyPreferenceRunsOnTheUsableConnectionOfAnyProviderThatCapacityChoosesAsync()
    {
        var flow = JobFlow.With();
        var other = new ConnectionName("other-harness");
        var selector = new FakeSelector(other);
        flow.Selectors.Add(selector);
        flow.Connections.Declared.AddRange(
        [
            Declared(Work),
            Declared(other) with { Provider = "other" },
            Declared(new ConnectionName("broken")),
            Declared(Personal),
        ]);
        flow.Connections.Refused["broken"] = ConnectionError.MissingFolder;

        var job = await flow.RunningAsync();

        var question = Assert.Single(selector.Questions);
        Assert.Equal([Work, other, Personal], question.Candidates);
        Assert.Equal(Assert.Single(flow.Defaults.Read), question.Worktree);
        Assert.Equal(Option<ConnectionName>.Some(other), Assert.Single(flow.Agents.Requests).Connection);
        Assert.Equal(Option<ConnectionName>.Some(other), job.Connection);
        var chosen = Assert.Single(flow.Bus.Published.OfType<ConnectionChosen>());
        Assert.Equal((job.Id, other, 3), (chosen.Job, chosen.Choice.Connection, chosen.Choice.Compared.Count));
        Assert.Equal(Option<ConnectionChoice>.Some(chosen.Choice), Outcomes.Present(await flow.Catalog.HistoryAsync(job.Id, TestContext.Current.CancellationToken)).Choice);
    }

    [Fact]
    public async Task AJobWithoutAnyPreferenceRunsOnTheFixedMachineDefaultWithoutAskingCapacityAsync()
    {
        var flow = JobFlow.With();
        var selector = new FakeSelector(Work);
        flow.Selectors.Add(selector);
        flow.Connections.Declared.AddRange([Declared(Work), Declared(Personal)]);
        flow.Connections.Fixed = Personal;

        await flow.RunningAsync();

        Assert.True(Assert.Single(flow.Agents.Requests).Connection.IsNone);
        Assert.Empty(selector.Questions);
        Assert.Empty(flow.Bus.Published.OfType<ConnectionChosen>());
    }

    [Fact]
    public async Task ThePreviewNamesTheConnectionTheRepositoryPrefersAtItsCurrentCommitWithoutAskingCapacityAsync()
    {
        var flow = JobFlow.With();
        var selector = new FakeSelector(Work);
        flow.Selectors.Add(selector);
        flow.Connections.Declared.AddRange([Declared(Work), Declared(Personal)]);
        flow.Defaults.Connection = Option<ConnectionName>.Some(Personal);

        var preview = Outcomes.Succeeds(await flow.Preview.PreviewAsync("/repositories/shop", Cancellation));

        Assert.Equal((ConnectionRoute.Repository, Option<ConnectionName>.Some(Personal), false), (preview.Route, preview.Connection, preview.Choice.IsSome));
        Assert.Equal(["/repositories/shop"], flow.Defaults.ReadCurrent);
        Assert.Empty(selector.Questions);
    }

    [Fact]
    public async Task ThePreviewOfAFixedMachineDefaultNamesItWithoutAskingCapacityAsync()
    {
        var flow = JobFlow.With();
        var selector = new FakeSelector(Work);
        flow.Selectors.Add(selector);
        flow.Connections.Declared.AddRange([Declared(Work), Declared(Personal)]);
        flow.Connections.Fixed = Personal;

        var preview = Outcomes.Succeeds(await flow.Preview.PreviewAsync("/repositories/shop", Cancellation));

        Assert.Equal((ConnectionRoute.MachineDefault, Option<ConnectionName>.Some(Personal)), (preview.Route, preview.Connection));
        Assert.Empty(selector.Questions);
    }

    [Fact]
    public async Task ThePreviewByCapacityAsksAtTheRepositorysCurrentCommitAndAnnouncesNothingAsync()
    {
        var flow = JobFlow.With();
        var selector = new FakeSelector(Personal);
        flow.Selectors.Add(selector);
        flow.Connections.Declared.AddRange([Declared(Work), Declared(Personal)]);

        var preview = Outcomes.Succeeds(await flow.Preview.PreviewAsync("/repositories/shop", Cancellation));

        Assert.Equal((ConnectionRoute.Capacity, Option<ConnectionName>.Some(Personal)), (preview.Route, preview.Connection));
        Assert.Equal([Work, Personal], Outcomes.Present(preview.Choice).Compared.Select(candidate => candidate.Connection));
        var question = Assert.Single(selector.Questions);
        Assert.Equal(("/repositories/shop", true), (question.Worktree, question.AtHead));
        Assert.Empty(flow.Bus.Published);
        Assert.Empty(flow.Store.Jobs);
    }

    [Fact]
    public async Task WithNothingToChooseByCapacityThePreviewFallsBackToTheMachineDefaultAsync()
    {
        var flow = JobFlow.With();
        flow.Connections.Declared.AddRange([Declared(Work), Declared(Personal)]);

        var preview = Outcomes.Succeeds(await flow.Preview.PreviewAsync("/repositories/shop", Cancellation));

        Assert.Equal((ConnectionRoute.Fallback, Option<ConnectionName>.Some(Work), false), (preview.Route, preview.Connection, preview.Choice.IsSome));
    }

    [Theory]
    [InlineData(true, "InvalidJobFile")]
    [InlineData(false, "UnusableConnection")]
    public async Task APreviewThatCannotBeMadeSaysWhyAsync(bool jobFileRejected, string rejection)
    {
        var flow = JobFlow.With();
        flow.Selectors.Add(new FakeSelector(Work));
        flow.Defaults.Connection = jobFileRejected
            ? Result<Option<ConnectionName>, JobRejection>.Failure(JobRejection.InvalidJobFile)
            : Option<ConnectionName>.None;
        flow.Connections.Rejection = jobFileRejected ? Option<ConnectionError>.None : ConnectionError.UnknownDefault;

        var preview = await flow.Preview.PreviewAsync("/repositories/shop", Cancellation);

        Assert.Equal(Enum.Parse<JobRejection>(rejection), Outcomes.FailsWith(preview));
    }

    private static DeclaredConnection Declared(ConnectionName name) => new(name, FakeConnections.Provider.Id, Option<string>.None);

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
