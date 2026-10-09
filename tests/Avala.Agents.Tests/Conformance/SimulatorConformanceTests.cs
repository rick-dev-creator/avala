using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
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
    [InlineData("follow-up")]
    [InlineData("near-limit")]
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
        var recording = await File.ReadAllTextAsync(RecordingFixtures.RecordingOf(name), Deadline);
        await File.WriteAllTextAsync(Path.Combine(recordings, $"{name}.json"), recording, Deadline);
        await using var services = Simulated(new AvalaPaths(data.Path));
        var recorded = JsonNode.Parse(recording)!;

        Assert.Empty(await AgentConformance.CheckTurnAsync(
            services.GetRequiredService<IAgentProvider>(),
            new SessionOptions(folder.Path, PermissionMode.AskEveryTime) { Tools = RecordedTools(recorded) },
            new UserTurn($"[replay: {name}] conformance"),
            AsRecorded(recorded),
            Deadline));
    }

    private static HarnessTool[] RecordedTools(JsonNode recording) =>
        [
            .. recording["options"]!["tools"]!.AsArray().Select(tool => (string)tool!["surface"]! == "canvas"
                ? AgentConformance.CanvasTool with { Name = (string)tool["name"]! }
                : AgentConformance.ExecutedTool with { Name = (string)tool["name"]! }),
        ];

    private static Func<PermissionRequested, PermissionDecision> AsRecorded(JsonNode recording)
    {
        var answers = recording["entries"]!.AsArray()
            .Select(entry => entry!["respond"])
            .OfType<JsonNode>()
            .ToDictionary(respond => (string)respond["item"]!, respond => respond);

        return requested => answers.TryGetValue(requested.Item.Value, out var respond)
            ? new PermissionDecision(requested.Item, (string)respond["answer"]! == "deny" ? PermissionAnswer.Deny : PermissionAnswer.Allow)
            {
                Message = respond["message"]?.GetValue<string>() is { } message ? message : Option<string>.None,
            }
            : new PermissionDecision(requested.Item, PermissionAnswer.Allow);
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

    [Fact]
    public async Task TheSimulatorDiscoversEachSimulatedLoginFolderAsAConnectionThatConformsAsync()
    {
        using var data = new TemporaryFolder();
        var logins = Path.Combine(data.Path, "simulated-logins");
        Directory.CreateDirectory(Path.Combine(logins, "work"));
        Directory.CreateDirectory(Path.Combine(logins, "personal account"));
        await using var services = Simulated(new AvalaPaths(data.Path));
        var discovery = services.GetRequiredService<IConnectionDiscovery>();

        Assert.Empty(await AgentConformance.CheckDiscoveryAsync(services.GetRequiredService<IAgentProvider>(), discovery, Deadline));
        Assert.Equal(
            [
                ("simulator-personal-account", "simulator", "login", Path.Combine(logins, "personal account")),
                ("simulator-work", "simulator", "login", Path.Combine(logins, "work")),
            ],
            (await discovery.DiscoverAsync(Deadline)).Select(found => (found.Name.Value, found.Provider, found.Credential.Source, found.Credential.Reference)));
    }

    [Fact]
    public async Task WithoutSimulatedLoginsTheSimulatorDiscoversNothingAsync()
    {
        using var data = new TemporaryFolder();
        await using var services = Simulated(new AvalaPaths(data.Path));

        Assert.Empty(await services.GetRequiredService<IConnectionDiscovery>().DiscoverAsync(Deadline));
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

    [Fact]
    public async Task TheSimulatorCallsAHarnessToolAndReportsTheResultItWasGivenAsync()
    {
        using var folder = new TemporaryFolder();
        await using var services = Simulated();

        Assert.Empty(await AgentConformance.CheckHarnessToolAsync(
            services.GetRequiredService<IAgentProvider>(),
            new SessionOptions(folder.Path, PermissionMode.AskEveryTime),
            new UserTurn("[simulate: follow-up] conformance"),
            Deadline));
    }

    [Fact]
    public async Task TheSimulatorHoldsTwoCallsAtOnceAndReportsEachResultInTheOrderItArrivesAsync()
    {
        using var folder = new TemporaryFolder();
        await using var services = Simulated();

        Assert.Empty(await AgentConformance.CheckParallelToolCallsAsync(
            services.GetRequiredService<IAgentProvider>(),
            new SessionOptions(folder.Path, PermissionMode.AskEveryTime),
            AgentConformance.ExecutedTool with { Name = "delegate" },
            new UserTurn("[simulate: delegate] conformance"),
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
    public async Task ReportsACanvasTheUnofferedCanvasScenarioDrawsInAMediaTypeTheToolDoesNotOfferAsync()
    {
        using var folder = new TemporaryFolder();
        await using var services = Simulated();

        Assert.Equal(
            ["the canvas flow was drawn in text/vnd.mermaid, which the canvas tool does not offer"],
            await AgentConformance.CheckCanvasToolAsync(
                services.GetRequiredService<IAgentProvider>(),
                new SessionOptions(folder.Path, PermissionMode.AskEveryTime),
                new UserTurn("[simulate: unoffered-canvas] conformance"),
                Deadline));
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

    [Theory]
    [InlineData("login")]
    [InlineData("apiKey")]
    public async Task TheSimulatorReportsTheUsageCostAndLimitsItsConnectionDeclaresAndNoOthersAsync(string credential)
    {
        using var folder = new TemporaryFolder();
        await using var services = Simulated();

        Assert.Empty(await CapabilityConformance.CheckReportsAsync(
            services.GetRequiredService<IAgentProvider>(),
            new SessionOptions(folder.Path, PermissionMode.AskEveryTime) { Connection = Credential(credential, "work") },
            new UserTurn("[simulate: near-limit] conformance"),
            Deadline));
    }

    [Theory]
    [InlineData("withoutCapabilities", "streamsPartialOutput", "reply")]
    [InlineData("withoutCapabilities", "exposesReasoning", "reply")]
    [InlineData("withoutCapabilities", "reportsUsage", "near-limit")]
    [InlineData("withoutCapabilities", "reportsCost", "near-limit")]
    [InlineData("withoutCapabilities", "reportsLimits", "near-limit")]
    [InlineData("withoutCapabilities", "resumable", "fix-after-feedback")]
    [InlineData("withoutCapabilities", "asksForms", "question")]
    [InlineData("withoutCapabilities", "acceptsTools", "follow-up")]
    [InlineData("withoutCapabilities", "interruptible", "reply")]
    [InlineData("withoutCapabilities", "acceptsMessagesMidTurn", "steer")]
    [InlineData("toolSurfaces", "executed", "canvas")]
    public async Task ASimulatorConnectionThatLacksAComponentBehavesAsItDeclaresAsync(string setting, string value, string scenario)
    {
        using var folder = new TemporaryFolder();
        await using var services = Simulated();
        var provider = services.GetRequiredService<IAgentProvider>();
        var options = new SessionOptions(folder.Path, PermissionMode.AskEveryTime)
        {
            Connection = new ConnectionEnvironment { Settings = new Dictionary<string, string> { [setting] = value } },
        };
        var instruction = new UserTurn($"[simulate: {scenario}] conformance");

        Assert.Empty(await (value switch
        {
            "resumable" => AgentConformance.CheckResumeAsync(provider, options, instruction, Deadline),
            "asksForms" => AgentConformance.CheckFormsAsync(provider, options, instruction, Deadline),
            "acceptsTools" => AgentConformance.CheckHarnessToolAsync(provider, options, instruction, Deadline),
            "executed" => AgentConformance.CheckCanvasToolAsync(provider, options, instruction, Deadline),
            "interruptible" => CapabilityConformance.CheckInterruptAsync(provider, options, instruction, Deadline),
            "acceptsMessagesMidTurn" => CapabilityConformance.CheckMidTurnAsync(provider, options, instruction, Deadline),
            _ => CapabilityConformance.CheckReportsAsync(provider, options, instruction, Deadline),
        }));
    }

    [Fact]
    public async Task TheSimulatorFoldsAMessageSentMidTurnIntoTheRunningTurnAsync()
    {
        using var folder = new TemporaryFolder();
        await using var services = Simulated();

        Assert.Empty(await CapabilityConformance.CheckMidTurnAsync(
            services.GetRequiredService<IAgentProvider>(),
            new SessionOptions(folder.Path, PermissionMode.AllowAll),
            new UserTurn("[simulate: steer] conformance"),
            Deadline));
    }

    [Fact]
    public async Task TheSimulatorEndsAHangingTurnItIsAskedToInterruptAsInterruptedAsync()
    {
        using var folder = new TemporaryFolder();
        await using var services = Simulated();

        Assert.Empty(await CapabilityConformance.CheckInterruptAsync(
            services.GetRequiredService<IAgentProvider>(),
            new SessionOptions(folder.Path, PermissionMode.AskEveryTime),
            new UserTurn("[simulate: hang] conformance"),
            Deadline));
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
