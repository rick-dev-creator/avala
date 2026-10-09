using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Simulator;
using Avala.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Agents.Tests.Conformance;

public sealed class SimulatorConformanceTests
{
    private static CancellationToken Deadline => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("reply")]
    [InlineData("edit")]
    [InlineData("fix-after-feedback")]
    [InlineData("rewrite-checks")]
    [InlineData("canvas")]
    [InlineData("permission")]
    [InlineData("repeated-permission")]
    [InlineData("question")]
    [InlineData("plan-approval")]
    public async Task AWellBehavedScenarioConformsAsync(string scenario)
    {
        using var folder = new TemporaryFolder();
        await using var services = Simulated();

        Assert.Empty(await CheckAsync(services, folder, scenario, Deadline));
    }

    public static TheoryData<string> Recordings => [.. RecordingFixtures.Names];

    [Theory]
    [MemberData(nameof(Recordings))]
    public async Task EveryCommittedRecordingConformsWhenTheSimulatorReplaysItAsync(string name)
    {
        using var folder = new TemporaryFolder();
        using var data = new TemporaryFolder();
        var recordings = Directory.CreateDirectory(Path.Combine(data.Path, "recordings")).FullName;
        await File.WriteAllTextAsync(
            Path.Combine(recordings, $"{name}.json"),
            await File.ReadAllTextAsync(RecordingFixtures.RecordingOf(name), Deadline),
            Deadline);
        await using var services = Simulated(new AvalaPaths(data.Path));

        Assert.Empty(await AgentConformance.CheckTurnAsync(
            services.GetRequiredService<IAgentProvider>(),
            new SessionOptions(folder.Path, PermissionMode.AskEveryTime),
            new UserTurn($"[replay: {name}] conformance"),
            Deadline));
    }

    [Fact]
    public async Task TheSimulatorIssuesAResumeTokenAndAcceptsItToContinueTheConversationAsync()
    {
        using var folder = new TemporaryFolder();
        await using var services = Simulated();

        Assert.Empty(await AgentConformance.CheckResumeAsync(
            services.GetRequiredService<IAgentProvider>(),
            new SessionOptions(folder.Path, PermissionMode.AskEveryTime),
            new UserTurn("[simulate: fix-after-feedback] conformance"),
            Deadline));
    }

    [Theory]
    [InlineData("login", "login")]
    [InlineData("login", "apiKey")]
    public async Task TwoConnectionsOfTheSimulatorShareNoSessionResumeTokenOrAccountAsync(string first, string second)
    {
        using var folder = new TemporaryFolder();
        await using var services = Simulated();
        var options = new SessionOptions(folder.Path, PermissionMode.AskEveryTime);

        Assert.Empty(await AgentConformance.CheckConnectionsAsync(
            services.GetRequiredService<IAgentProvider>(),
            options with { Connection = Credential(first, "work") },
            options with { Connection = Credential(second, "personal") },
            new UserTurn("[simulate: fix-after-feedback] conformance"),
            Deadline));
    }

    private static ConnectionEnvironment Credential(string kind, string name) =>
        kind == "login"
            ? new ConnectionEnvironment { ConfigurationDirectory = Path.Combine("/logins", name) }
            : new ConnectionEnvironment { ApiKey = new Secret($"sk-{name}") };

    [Fact]
    public async Task TheSimulatorDrawsTheCanvasScenarioThroughTheCanvasToolAsync()
    {
        using var folder = new TemporaryFolder();
        await using var services = Simulated();

        Assert.Empty(await AgentConformance.CheckCanvasToolAsync(
            services.GetRequiredService<IAgentProvider>(),
            new SessionOptions(folder.Path, PermissionMode.AskEveryTime),
            new UserTurn("[simulate: canvas] conformance"),
            Deadline));
    }

    [Theory]
    [InlineData("question")]
    [InlineData("plan-approval")]
    public async Task TheSimulatorAsksWellFormedFormsAndRefusesAnswersToFormsThatAreNotOpenAsync(string scenario)
    {
        using var folder = new TemporaryFolder();
        await using var services = Simulated();

        Assert.Empty(await AgentConformance.CheckFormsAsync(
            services.GetRequiredService<IAgentProvider>(),
            new SessionOptions(folder.Path, PermissionMode.AskEveryTime),
            new UserTurn($"[simulate: {scenario}] conformance"),
            Deadline));
    }

    [Fact]
    public async Task TheSimulatorStartsTheProcessesOfAScenarioThroughTheSessionsLauncherAsync()
    {
        await using var folder = new TemporaryFolder();
        await using var services = Simulated();

        Assert.Empty(await AgentConformance.CheckProcessesAsync(
            services.GetRequiredService<IAgentProvider>(),
            new SessionOptions(folder.Path, PermissionMode.AskEveryTime),
            new UserTurn("[simulate: processes] conformance"),
            Deadline));
    }

    [Fact]
    public async Task TheSimulatorHonorsADenialWithAMessageAsync()
    {
        using var folder = new TemporaryFolder();
        await using var services = Simulated();

        Assert.Empty(await AgentConformance.CheckDenialAsync(
            services.GetRequiredService<IAgentProvider>(),
            new SessionOptions(folder.Path, PermissionMode.AskEveryTime),
            new UserTurn("[simulate: permission] conformance"),
            Deadline));
    }

    [Fact]
    public async Task ReportsTheItemTheLeftOpenScenarioLeavesOpenAsync()
    {
        using var folder = new TemporaryFolder();
        await using var services = Simulated();

        Assert.Equal(["item build was left open"], await CheckAsync(services, folder, "left-open", Deadline));
    }

    [Fact]
    public async Task ReportsThatTheHangScenarioNeverCompletesAsync()
    {
        using var folder = new TemporaryFolder();
        await using var services = Simulated();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Deadline);

        var check = CheckAsync(services, folder, "hang", deadline.Token);
        await deadline.CancelAsync();

        Assert.Equal(["the turn did not complete before the deadline"], await check);
    }

    private static Task<IReadOnlyList<string>> CheckAsync(
        ServiceProvider services,
        TemporaryFolder folder,
        string scenario,
        CancellationToken deadline) =>
        AgentConformance.CheckTurnAsync(
            services.GetRequiredService<IAgentProvider>(),
            new SessionOptions(folder.Path, PermissionMode.AskEveryTime),
            new UserTurn($"[simulate: {scenario}] conformance"),
            deadline);

    private static ServiceProvider Simulated() => Simulated(new AvalaPaths(Path.Combine(Path.GetTempPath(), "avala-conformance")));

    private static ServiceProvider Simulated(AvalaPaths data)
    {
        var services = new ServiceCollection().AddSingleton(data);
        new SimulatorPlugin(TimeSpan.Zero).Register(new Registrar(services));

        return services.BuildServiceProvider();
    }

    private sealed class Registrar(IServiceCollection services) : IPluginRegistrar
    {
        public IServiceCollection Services { get; } = services;
    }
}
