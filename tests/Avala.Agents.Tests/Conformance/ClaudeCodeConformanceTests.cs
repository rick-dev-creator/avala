using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.ClaudeCode;
using Avala.ClaudeCode.Replay;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Agents.Tests.Conformance;

public sealed class ClaudeCodeConformanceTests
{
    private const string WorkLogin = ".claude-work";

    private const string SecondLogin = ".claude";

    private static readonly UserTurn Instruction = new("conformance");

    private static CancellationToken Deadline => TestContext.Current.CancellationToken;

    public static string Transcripts(string name) => Path.Combine(Repository.Root.FullName, "tests", "transcripts", "claude-code", name);

    [Fact]
    public async Task ARecordedEditJobDrawsItsCanvasAndAsksBeforeEveryCommandAsync()
    {
        await using var replay = await ReplayAsync("edit");

        Assert.Empty(await AgentConformance.CheckCanvasToolAsync(replay.Provider, replay.Options(WorkLogin), Instruction, Deadline));
    }

    [Fact]
    public async Task ARecordedLoginReportsTheUsageCostAndLimitWindowsItDeclaresAsync()
    {
        await using var replay = await ReplayAsync("edit");

        Assert.Empty(await CapabilityConformance.CheckReportsAsync(
            replay.Provider,
            replay.Options(WorkLogin) with { Tools = [AgentConformance.CanvasTool] },
            Instruction,
            Deadline));
    }

    [Fact]
    public async Task ARecordedSessionStartsClaudeCodeThroughTheSessionsLauncherAsync()
    {
        await using var replay = await ReplayAsync("edit");

        Assert.Empty(await AgentConformance.CheckProcessesAsync(replay.Provider, replay.Options(WorkLogin) with { Tools = [AgentConformance.CanvasTool] }, Instruction, Deadline));
    }

    [Fact]
    public async Task ARecordedQuestionIsAWellFormedFormThatRefusesAnswersToFormsThatAreNotOpenAsync()
    {
        await using var replay = await ReplayAsync("question");

        Assert.Empty(await AgentConformance.CheckFormsAsync(replay.Provider, replay.Options(WorkLogin), Instruction, Deadline));
    }

    [Fact]
    public async Task ARecordedDenialIsHonoredAsync()
    {
        await using var replay = await ReplayAsync("denial");

        Assert.Empty(await AgentConformance.CheckDenialAsync(replay.Provider, replay.Options(WorkLogin), Instruction, Deadline));
    }

    [Fact]
    public async Task ARecordedHarnessToolCallReportsTheResultItWasGivenAsync()
    {
        await using var replay = await ReplayAsync("sessions");

        Assert.Empty(await AgentConformance.CheckHarnessToolAsync(replay.Provider, replay.Options(WorkLogin), Instruction, Deadline));
    }

    [Fact]
    public async Task ARecordedConversationIssuesATokenThatResumesItAsync()
    {
        await using var replay = await ReplayAsync("sessions");

        Assert.Empty(await AgentConformance.CheckResumeAsync(
            replay.Provider,
            replay.Options(WorkLogin) with { Tools = [AgentConformance.ExecutedTool] },
            Instruction,
            Deadline));
    }

    [Fact]
    public async Task TwoRecordedLoginsShareNoSessionAccountOrResumeTokenAsync()
    {
        await using var replay = await ReplayAsync("sessions");

        Assert.Empty(await AgentConformance.CheckConnectionsAsync(
            replay.Provider,
            replay.Options(WorkLogin) with { Tools = [AgentConformance.ExecutedTool] },
            replay.Options(SecondLogin),
            Instruction,
            Deadline));
    }

    [Fact]
    public async Task ClaudeCodeDiscoversItsLoginsAsReferencesAsync()
    {
        using var home = new TemporaryFolder();

        foreach (var login in new[] { SecondLogin, WorkLogin, ".claude-personal" })
        {
            await File.WriteAllTextAsync(Path.Combine(Directory.CreateDirectory(Path.Combine(home.Path, login)).FullName, ".credentials.json"), "{}", Deadline);
        }

        await using var composition = PluginComposition.Of(
            new ClaudeCodePlugin("claude", [], home.Path, [Path.Combine(home.Path, WorkLogin)]),
            new AvalaPaths(home.Path),
            _ => { });

        Assert.Empty(await AgentConformance.CheckDiscoveryAsync(composition.Get<IAgentProvider>(), composition.Get<IConnectionDiscovery>(), Deadline));
        Assert.Equal(3, (await composition.Get<IConnectionDiscovery>().DiscoverAsync(Deadline)).Count);
    }

    private static async Task<Replay> ReplayAsync(string name)
    {
        var replay = new Replay(Transcripts(name));
        await replay.PrepareAsync();

        return replay;
    }

    private sealed class Replay(string transcripts) : IAsyncDisposable
    {
        private readonly TemporaryFolder home = new();
        private readonly TemporaryFolder workingDirectory = new();
        private readonly PluginComposition composition = PluginComposition.Of(
            new ClaudeCodePlugin("dotnet", [typeof(TranscriptPlayer).Assembly.Location, transcripts]),
            new AvalaPaths(Path.Combine(Path.GetTempPath(), "avala-conformance")),
            _ => { });

        public IAgentProvider Provider => composition.Get<IAgentProvider>();

        public SessionOptions Options(string login) =>
            new(workingDirectory.Path, PermissionMode.AskEveryTime)
            {
                Connection = new ConnectionEnvironment { ConfigurationDirectory = Path.Combine(home.Path, login) },
            };

        public async Task PrepareAsync()
        {
            foreach (var (login, index) in new[] { WorkLogin, SecondLogin }.Select((login, index) => (login, index)))
            {
                var folder = Directory.CreateDirectory(Path.Combine(home.Path, login, "projects", "replayed")).Parent!.Parent!.FullName;
                await File.WriteAllTextAsync(
                    Path.Combine(folder, ".claude.json"),
                    $$"""{ "oauthAccount": { "accountUuid": "account-{{index}}", "emailAddress": "{{login}}@example.com" } }""",
                    Deadline);
            }

            foreach (var transcript in Directory.EnumerateFiles(transcripts, "*.jsonl"))
            {
                var header = JsonNode.Parse((await File.ReadAllLinesAsync(transcript, Deadline))[0])!;

                if (header["resume"]?.GetValue<string>() is { } resumed && header["folder"]?.GetValue<string>() is { } login)
                {
                    await File.WriteAllTextAsync(Path.Combine(home.Path, login, "projects", "replayed", $"{resumed}.jsonl"), "{}", Deadline);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await composition.DisposeAsync();
            await workingDirectory.DisposeAsync();
            await home.DisposeAsync();
        }
    }
}
