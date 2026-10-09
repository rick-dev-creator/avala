using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.ClaudeCode.Conversations;
using Avala.ClaudeCode.Folders;
using Avala.ClaudeCode.Protocol;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;

namespace Avala.ClaudeCode.Tests.Conversations;

public sealed class ProviderTests
{
    private static readonly Guid Held = Guid.Parse(Cli.Session);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASessionStartsTheCliInStreamJsonModeWithAvalasServerAndItsPermissionPromptAsync()
    {
        using var folder = new TemporaryFolder();
        var cli = new FakeCli();

        await using var session = Started(await Provider(cli).StartAsync(new SessionOptions(folder.Path, PermissionMode.AskEveryTime), Cancellation));
        var arguments = cli.Launches.Single().Arguments;

        Assert.Equal(folder.Path, cli.Launches.Single().WorkingDirectory);
        Assert.Contains("--include-partial-messages", arguments);
        Assert.Equal("mcp__avala__permission_prompt", After(arguments, "--permission-prompt-tool"));
        Assert.Equal("""{"mcpServers":{"avala":{"type":"sdk","name":"avala","timeout":2147483647,"disableAutoBackground":true}}}""", After(arguments, "--mcp-config"));
        Assert.Equal("default", After(arguments, "--permission-mode"));
        Assert.Equal("initialize", (string?)JsonNode.Parse(await cli.Written.Reader.ReadAsync(Cancellation))!["request"]!["subtype"]);
    }

    [Fact]
    public async Task ASessionAsksForSubagentTextAndLetsHarnessCallsWaitAsLongAsTheCliAllowsAsync()
    {
        using var folder = new TemporaryFolder();
        var cli = new FakeCli();

        await using var session = Started(await Provider(cli).StartAsync(new SessionOptions(folder.Path, PermissionMode.AskEveryTime), Cancellation));
        var launch = cli.Launches.Single();

        Assert.Contains("--forward-subagent-text", launch.Arguments);
        Assert.Equal("2147483647", launch.Variables["MCP_TOOL_TIMEOUT"]);
        Assert.Equal(2147483647, JsonNode.Parse(After(launch.Arguments, "--mcp-config")!)!["mcpServers"]!["avala"]!["timeout"]!.GetValue<int>());
        Assert.Contains("CLAUDE_AUTO_BACKGROUND_TASKS", launch.Cleared);
        Assert.Contains("CLAUDE_CODE_MCP_AUTO_BACKGROUND_MS", launch.Cleared);
    }

    [Theory]
    [InlineData(null, null, "user,project,local", true)]
    [InlineData("true", "false", "user,project,local", true)]
    [InlineData("true", "true", "user,project,local", false)]
    [InlineData("false", null, "", false)]
    [InlineData("false", "true", "", false)]
    public async Task TheConnectionDecidesWhetherTheUsersConfigurationAndHooksLoadAsync(string? configuration, string? hooks, string sources, bool hooksOff)
    {
        using var folder = new TemporaryFolder();
        var cli = new FakeCli();
        var settings = new Dictionary<string, string?> { ["userConfiguration"] = configuration, ["userHooks"] = hooks }
            .Where(setting => setting.Value is not null)
            .ToDictionary(setting => setting.Key, setting => setting.Value!);
        var connection = new ConnectionEnvironment { Settings = settings };

        await using var session = Started(await Provider(cli).StartAsync(new SessionOptions(folder.Path, PermissionMode.AskEveryTime) { Connection = connection }, Cancellation));
        var arguments = cli.Launches.Single().Arguments;

        Assert.Equal(sources, After(arguments, "--setting-sources"));
        Assert.Equal(sources.Length == 0, arguments.Contains("--strict-mcp-config"));
        Assert.Equal(hooksOff, Settings(arguments)?["disableAllHooks"]?.GetValue<bool>() ?? false);
        Assert.Equal("""{"mcpServers":{"avala":{"type":"sdk","name":"avala","timeout":2147483647,"disableAutoBackground":true}}}""", After(arguments, "--mcp-config"));
    }

    [Fact]
    public async Task TheConnectionAloneDecidesTheAccountTheCliRunsUnderAsync()
    {
        using var folder = new TemporaryFolder();
        var cli = new FakeCli();
        var connection = new ConnectionEnvironment
        {
            ConfigurationDirectory = "/logins/work",
            Settings = new Dictionary<string, string> { ["model"] = "haiku" },
        };

        await using var session = Started(await Provider(cli).StartAsync(new SessionOptions(folder.Path, PermissionMode.AskEveryTime) { Connection = connection }, Cancellation));
        var launch = cli.Launches.Single();

        Assert.Equal("/logins/work", launch.Variables["CLAUDE_CONFIG_DIR"]);
        Assert.False(launch.Variables.ContainsKey("ANTHROPIC_API_KEY"));
        Assert.Contains("ANTHROPIC_API_KEY", launch.Cleared);
        Assert.Contains("CLAUDECODE", launch.Cleared);
        Assert.Equal("haiku", After(launch.Arguments, "--model"));
        Assert.Equal(
            [$"{Path.Combine(Path.GetTempPath(), "avala-no-home", ".claude").Replace('\\', '/')}/**"],
            Settings(launch.Arguments)!["claudeMdExcludes"]!.AsArray().Select(exclude => (string?)exclude));
    }

    [Fact]
    public async Task TheDefaultConfigurationFolderLeavesTheCliOnItsOwnDefaultAsync()
    {
        using var home = await HomeAsync();
        var cli = new FakeCli();
        var connection = new ConnectionEnvironment { ConfigurationDirectory = Path.Combine(home.Path, ".claude") };

        await using var session = Started(await Provider(cli, home.Path).StartAsync(new SessionOptions(home.Path, PermissionMode.AskEveryTime) { Connection = connection }, Cancellation));

        Assert.False(cli.Launches.Single().Variables.ContainsKey("CLAUDE_CONFIG_DIR"));
        Assert.Contains("CLAUDE_CONFIG_DIR", cli.Launches.Single().Cleared);
        Assert.Null(Settings(cli.Launches.Single().Arguments)!["claudeMdExcludes"]);
    }

    [Fact]
    public async Task AnApiKeyConnectionHandsTheKeyToTheCliAndNoConfigurationFolderAsync()
    {
        using var folder = new TemporaryFolder();
        var cli = new FakeCli();
        var connection = new ConnectionEnvironment { ApiKey = new Secret("sk-test") };

        await using var session = Started(await Provider(cli).StartAsync(new SessionOptions(folder.Path, PermissionMode.AskEveryTime) { Connection = connection }, Cancellation));

        Assert.Equal("sk-test", cli.Launches.Single().Variables["ANTHROPIC_API_KEY"]);
        Assert.False(cli.Launches.Single().Variables.ContainsKey("CLAUDE_CONFIG_DIR"));
        Assert.Equal(string.Empty, After(cli.Launches.Single().Arguments, "--setting-sources"));
        Assert.Contains("--strict-mcp-config", cli.Launches.Single().Arguments);
        Assert.StartsWith("api-key:", session.Account.Match(account => account.Id, () => string.Empty), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("--dangerously-skip-permissions/0")]
    [InlineData("4d81cdb1-40e2-4ee5-ab3f-000000000000/0")]
    public async Task ATokenThisConnectionDoesNotHoldIsRefusedWithoutStartingTheCliAsync(string token)
    {
        using var home = await HomeAsync();
        var cli = new FakeCli();

        var started = await Provider(cli, home.Path).StartAsync(
            new SessionOptions(home.Path, PermissionMode.AskEveryTime) { Resume = new ResumeToken(token) },
            Cancellation);

        Assert.Equal(AgentError.CannotResume, started.Match(_ => default, error => error));
        Assert.Empty(cli.Launches);
    }

    [Fact]
    public async Task AHeldConversationResumesThroughTheCliAsync()
    {
        using var home = await HomeAsync();
        var cli = new FakeCli();

        await using var session = Started(await Provider(cli, home.Path).StartAsync(
            new SessionOptions(home.Path, PermissionMode.AskEveryTime) { Resume = new ConversationMark(Held, 0.5m).Token },
            Cancellation));

        Assert.Equal(Cli.Session, After(cli.Launches.Single().Arguments, "--resume"));
    }

    [Fact]
    public async Task TheAccountIsTheLoginOfTheConfigurationFolderAsync()
    {
        using var home = await HomeAsync();
        var folders = new ConfigurationFolders(new UserHome(home.Path, []));

        var account = await folders.AccountAsync(new ConnectionEnvironment { ConfigurationDirectory = Path.Combine(home.Path, ".claude") }, Cancellation);
        var none = await folders.AccountAsync(new ConnectionEnvironment { ConfigurationDirectory = Path.Combine(home.Path, "missing") }, Cancellation);

        Assert.Equal(new AgentAccount("claude:account-1", "ana@example.com"), account);
        Assert.True(none.IsNone);
    }

    [Fact]
    public async Task ACliThatCannotStartLeavesTheProviderUnavailableAsync()
    {
        using var folder = new TemporaryFolder();

        var started = await Provider(new FakeCli { Refusal = AgentError.ProviderUnavailable }).StartAsync(
            new SessionOptions(folder.Path, PermissionMode.AskEveryTime),
            Cancellation);

        Assert.Equal(AgentError.ProviderUnavailable, started.Match(_ => default, error => error));
    }

    [Fact]
    public async Task ACliThatEndsOnItsOwnEndsTheEventStreamAsACrashAsync()
    {
        using var folder = new TemporaryFolder();
        var cli = new FakeCli();
        await using var session = Started(await Provider(cli).StartAsync(new SessionOptions(folder.Path, PermissionMode.AskEveryTime), Cancellation));

        cli.Output.Writer.TryComplete();

        await Assert.ThrowsAsync<IOException>(async () =>
        {
            await foreach (var _ in session.Events.WithCancellation(Cancellation))
            {
            }
        });
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void ASubscriptionLoginReportsItsLimitWindowsAndAnApiKeyDoesNot(bool folder, bool key, bool limits)
    {
        var connection = new ConnectionEnvironment
        {
            ConfigurationDirectory = folder ? "/logins/work" : Option<string>.None,
            ApiKey = key ? new Secret("sk-ant-1") : Option<Secret>.None,
        };

        var declared = Provider(new FakeCli()).CapabilitiesOn(connection);

        Assert.Equal(limits ? new ReportsLimits(["5h", "7d", "7d opus", "7d sonnet"]) : Option<ReportsLimits>.None, declared.Get<ReportsLimits>());
        Assert.Equal(
            CapabilitySet.Of(
                new StreamsPartialOutput(),
                new ExposesReasoning(),
                new Interruptible(),
                new Resumable(),
                new AcceptsTools([ToolSurface.Canvas, ToolSurface.Executed]),
                new AsksForms(),
                new AcceptsMessagesMidTurn(),
                new ReportsUsage(),
                new ReportsCost("USD")),
            declared.Without<ReportsLimits>());
    }

    private static ClaudeCodeProvider Provider(FakeCli cli, string? home = null) =>
        Provider(cli, new UserHome(home ?? Path.Combine(Path.GetTempPath(), "avala-no-home"), []));

    private static ClaudeCodeProvider Provider(FakeCli cli, UserHome home) => new(cli, new ConfigurationFolders(home), home);

    private static IAgentSession Started(Result<IAgentSession, AgentError> started) =>
        started.Match(session => session, error => throw new InvalidOperationException(error.ToString()));

    private static string? After(IReadOnlyList<string> arguments, string flag) =>
        arguments.SkipWhile(argument => argument != flag).Skip(1).FirstOrDefault();

    private static JsonNode? Settings(IReadOnlyList<string> arguments) =>
        After(arguments, "--settings") is { } settings ? JsonNode.Parse(settings) : null;

    private static async Task<TemporaryFolder> HomeAsync()
    {
        var home = new TemporaryFolder();
        var folder = Directory.CreateDirectory(Path.Combine(home.Path, ".claude", "projects", "-work")).Parent!.Parent!.FullName;
        await File.WriteAllTextAsync(Path.Combine(folder, "projects", "-work", $"{Cli.Session}.jsonl"), "{}", Cancellation);
        await File.WriteAllTextAsync(
            Path.Combine(folder, ".claude.json"),
            """{ "oauthAccount": { "accountUuid": "account-1", "emailAddress": "ana@example.com" } }""",
            Cancellation);

        return home;
    }

    private sealed class FakeCli : ICli
    {
        public List<CliLaunch> Launches { get; } = [];

        public Channel<string> Output { get; } = Channel.CreateUnbounded<string>();

        public Channel<string> Written { get; } = Channel.CreateUnbounded<string>();

        public AgentError? Refusal { get; init; }

        public Result<ICliProcess, AgentError> Start(CliLaunch launch, IProcessLauncher processes, Option<string> transcripts)
        {
            if (Refusal is { } refusal)
            {
                return refusal;
            }

            Launches.Add(launch);

            return new Process(this);
        }

        private sealed class Process(FakeCli cli) : ICliProcess
        {
            public IAsyncEnumerable<string> Lines => ReadAsync(CancellationToken.None);

            public Task WriteAsync(string line, CancellationToken cancellationToken) => cli.Written.Writer.WriteAsync(line, cancellationToken).AsTask();

            public ValueTask DisposeAsync()
            {
                cli.Output.Writer.TryComplete();

                return ValueTask.CompletedTask;
            }

            private async IAsyncEnumerable<string> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await foreach (var line in cli.Output.Reader.ReadAllAsync(cancellationToken))
                {
                    yield return line;
                }
            }
        }
    }
}
