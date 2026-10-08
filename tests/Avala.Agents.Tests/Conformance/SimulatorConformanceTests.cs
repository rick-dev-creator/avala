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
    [InlineData("canvas")]
    [InlineData("permission")]
    public async Task AWellBehavedScenarioConformsAsync(string scenario)
    {
        using var folder = new TemporaryFolder();
        await using var services = Simulated();

        Assert.Empty(await CheckAsync(services, folder, scenario, Deadline));
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
            new SessionOptions(folder.Path, PermissionMode.AllowEdits),
            new UserTurn($"[simulate: {scenario}] conformance"),
            deadline);

    private static ServiceProvider Simulated()
    {
        var services = new ServiceCollection();
        new SimulatorPlugin(TimeSpan.Zero).Register(new Registrar(services));

        return services.BuildServiceProvider();
    }

    private sealed class Registrar(IServiceCollection services) : IPluginRegistrar
    {
        public IServiceCollection Services { get; } = services;
    }
}
