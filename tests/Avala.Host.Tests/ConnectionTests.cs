using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class ConnectionTests(PublishedPlugins plugins)
{
    private const string Connections = """
        {
          "default": "work",
          "connections": [
            { "name": "work", "provider": "simulator", "credential": { "source": "login" } },
            { "name": "personal", "provider": "simulator", "credential": { "source": "login" } }
          ]
        }
        """;

    private static readonly (string File, string Content)[] TwoLogins =
    [
        ("connections.json", Connections),
        ("connections/work/.login", string.Empty),
        ("connections/personal/.login", string.Empty),
    ];

    [Fact]
    public async Task TwoJobsOnTwoConnectionsOfTheSimulatorRunSideBySideWithTheirUsageApartAsync()
    {
        await using var run = await SimulatedRun.ConnectedAsync(plugins, "reply", Work, TwoLogins);
        var other = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("reply")) { Connection = Personal }));

        Assert.Equal([JobStatus.AwaitingReview, JobStatus.AwaitingReview], await run.SettledAsync(run.Job, other));
        await run.UsageRecordedAsync(reports: 4);

        var usage = run.Get<IUsage>();
        var reply = new TokenUsage(1_200, 80, 600, 120, 20);
        Assert.Equal(
            [("personal", "simulator", reply, 1), ("work", "simulator", reply, 1)],
            usage.ByConnection().Select(used => (used.Connection.Value, used.Provider.Id, used.Usage.Tokens, used.Usage.Limits.Count)));
        Assert.Equal(
            [Login(run, "personal"), Login(run, "work")],
            usage.ByAccount().Select(used => used.Account.Id).Order(StringComparer.Ordinal));
        Assert.Equal([reply, reply], new[] { run.Job, other }.Select(job => Outcomes.Present(usage.OfJob(job)).Tokens));
    }

    [Fact]
    public async Task AJobRecoveredAfterARestartKeepsItsConnectionAndResumesItsConversationThereAsync()
    {
        await using var run = await SimulatedRun.ConnectedAsync(plugins, "hang", Personal, TwoLogins);
        Assert.Equal(Personal, (await run.OpenedAsync()).Connection);
        await run.ResumableAsync();

        await run.RestartAsync();

        var recovered = await run.OpenedAsync();
        var turn = await run.TurnAsync();
        Assert.Equal((Personal, Option<AgentAccount>.Some(new AgentAccount(Login(run, "personal"), "Simulated account (personal)"))), (recovered.Connection, recovered.Account));
        Assert.Contains(turn, update => update is ItemProgressed { Text: "Finished what I was doing." });
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
    }

    [Fact]
    public async Task AJobWithoutAConnectionRunsOnTheDefaultConnectionItsRepositoryDeclaresAsync()
    {
        await using var run = await SimulatedRun.ConnectedAsync(
            plugins,
            "reply",
            Option<ConnectionName>.None,
            TwoLogins,
            (".avala/jobs.json", """{ "connection": "personal" }"""));

        Assert.Equal(Personal, (await run.OpenedAsync()).Connection);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
    }

    [Fact]
    public async Task AJobNamingAnUnknownConnectionIsRejectedAsync()
    {
        await using var run = await SimulatedRun.ConnectedAsync(plugins, "reply", Work, TwoLogins);

        var rejected = await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("reply")) { Connection = new ConnectionName("nowhere") });

        Assert.Equal(JobRejection.UnknownConnection, Outcomes.FailsWith(rejected));
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
    }

    private static ConnectionName Work => new("work");

    private static ConnectionName Personal => new("personal");

    private static string Login(SimulatedRun run, string connection) =>
        $"simulated-login:{Path.Combine(run.DataFolder, "connections", connection)}";
}
