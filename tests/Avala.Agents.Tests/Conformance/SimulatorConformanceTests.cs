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
