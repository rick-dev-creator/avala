using System.Collections.Immutable;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Simulator.Playback;
using Avala.Simulator.Recordings;
using Avala.Simulator.Tests.Recordings;
using Avala.Testing;

namespace Avala.Simulator.Tests.Playback;

public sealed class ConnectionTests : IDisposable
{
    private readonly TemporaryFolder data = new();
    private readonly TemporaryFolder folder = new();
    private readonly SimulatedProvider provider;

    public ConnectionTests() => provider = new SimulatedProvider(Stage.Crafted(new AvalaPaths(data.Path)));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EverySessionReportsTheSimulatedAccountOfItsConnectionsCredentialAsync()
    {
        var accounts = new List<Option<AgentAccount>>();

        foreach (var connection in new[]
        {
            Login("/logins/work"),
            Login("/logins/personal"),
            new ConnectionEnvironment { ApiKey = new Secret("sk-secret") },
            ConnectionEnvironment.Default,
        })
        {
            await using var session = Outcomes.Succeeds(await provider.StartAsync(Options(connection), Cancellation));
            accounts.Add(session.Account);
        }

        Assert.Equal(new AgentAccount("simulated-login:/logins/work", "Simulated account (work)"), Outcomes.Present(accounts[0]));
        Assert.Equal(new AgentAccount("simulated-login:/logins/personal", "Simulated account (personal)"), Outcomes.Present(accounts[1]));
        Assert.StartsWith("simulated-key:", Outcomes.Present(accounts[2]).Id, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-secret", Outcomes.Present(accounts[2]).Id, StringComparison.Ordinal);
        Assert.Equal(SimulatedAccounts.Default, Outcomes.Present(accounts[3]));
    }

    [Fact]
    public async Task AResumeTokenContinuesItsConversationOnlyOnTheAccountThatIssuedItAsync()
    {
        var token = await IssuedTokenAsync(Login("/logins/work"));

        var elsewhere = await provider.StartAsync(Options(Login("/logins/personal")) with { Resume = token }, Cancellation);
        await using var resumed = Outcomes.Succeeds(await provider.StartAsync(Options(Login("/logins/work")) with { Resume = token }, Cancellation));

        Assert.Equal(AgentError.CannotResume, Outcomes.FailsWith(elsewhere));
        Outcomes.Succeeds(await resumed.SendAsync(new UserTurn("Two tests fail"), Cancellation));
        Assert.Contains(await Stage.ReadUntilAsync<TurnCompleted>(resumed, Cancellation), agentEvent => agentEvent is ItemStarted { Item.Value: "fix" });
    }

    [Fact]
    public async Task AConnectionThatNamesARecordingReplaysItFromTheFirstMessageWithTheRecordedAccountAsync()
    {
        Directory.CreateDirectory(Path.Combine(data.Path, RecordingFolder.FolderName));
        await File.WriteAllTextAsync(
            Path.Combine(data.Path, RecordingFolder.FolderName, "recorded.json"),
            Recorded.SessionOf("""{ "id": "account-7", "label": "Recorded account" }""", Recorded.TurnStarted, Recorded.Finished),
            Cancellation);
        var replaying = new ConnectionEnvironment { Settings = ImmutableDictionary<string, string>.Empty.Add(SimulatedAccounts.ReplaySetting, "recorded") };

        await using var session = Outcomes.Succeeds(await provider.StartAsync(
            Options(replaying) with { Permissions = PermissionMode.AskEveryTime },
            Cancellation));
        Outcomes.Succeeds(await session.SendAsync(new UserTurn("[simulate: edit] Greet the team"), Cancellation));

        Assert.Equal(new AgentAccount("account-7", "Recorded account"), Outcomes.Present(session.Account));
        Assert.Equal(
            ["TurnStarted", "TurnCompleted"],
            (await Stage.ReadUntilAsync<TurnCompleted>(session, Cancellation)).Select(agentEvent => agentEvent.GetType().Name));
    }

    [Theory]
    [InlineData("streamsPartialOutput", typeof(StreamsPartialOutput))]
    [InlineData("exposesReasoning", typeof(ExposesReasoning))]
    [InlineData("interruptible", typeof(Interruptible))]
    [InlineData("resumable", typeof(Resumable))]
    [InlineData("acceptsTools", typeof(AcceptsTools))]
    [InlineData("asksForms", typeof(AsksForms))]
    [InlineData("reportsUsage", typeof(ReportsUsage), typeof(ReportsCost))]
    [InlineData("reportsCost", typeof(ReportsCost))]
    [InlineData(" reportsLimits , unknown", typeof(ReportsLimits))]
    public void AConnectionDeclaresEveryComponentButTheOnesItsSettingsLeaveOut(string without, params Type[] removed)
    {
        var declared = provider.CapabilitiesOn(Login("/logins/work"));

        var tailored = provider.CapabilitiesOn(Login("/logins/work") with { Settings = ImmutableDictionary<string, string>.Empty.Add("withoutCapabilities", without) });

        Assert.Equal(
            [.. declared.Components.Where(component => !removed.Contains(component.GetType()))],
            tailored.Components);
    }

    [Fact]
    public void AnApiKeyReportsNoLimitWindowsAndToolSurfacesRefineTheToolsAccepted()
    {
        var keyed = provider.CapabilitiesOn(new ConnectionEnvironment { ApiKey = new Secret("sk-secret") });
        var executedOnly = provider.CapabilitiesOn(ConnectionEnvironment.Default with
        {
            Settings = ImmutableDictionary<string, string>.Empty.Add("toolSurfaces", "executed, nowhere"),
        });

        Assert.Equal(
            (false, true, Option<AcceptsTools>.Some(new AcceptsTools([ToolSurface.Executed]))),
            (keyed.Has<ReportsLimits>(), provider.CapabilitiesOn(ConnectionEnvironment.Default).Has<ReportsLimits>(), executedOnly.Get<AcceptsTools>()));
    }

    public void Dispose()
    {
        data.Dispose();
        folder.Dispose();
    }

    private async Task<ResumeToken> IssuedTokenAsync(ConnectionEnvironment connection)
    {
        await using var session = Outcomes.Succeeds(await provider.StartAsync(Options(connection), Cancellation));
        Outcomes.Succeeds(await session.SendAsync(new UserTurn("[simulate: fix-after-feedback] Fix the calculator"), Cancellation));

        return (await Stage.ReadUntilAsync<TurnCompleted>(session, Cancellation)).OfType<ResumeTokenIssued>().Last().Token;
    }

    private static ConnectionEnvironment Login(string folder) => new() { ConfigurationDirectory = folder };

    private SessionOptions Options(ConnectionEnvironment connection) => new(folder.Path, PermissionMode.AllowAll) { Connection = connection };
}
