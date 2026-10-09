using System.Runtime.CompilerServices;
using System.Threading.Channels;
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
        Assert.Equal("""{"mcpServers":{"avala":{"type":"sdk","name":"avala"}}}""", After(arguments, "--mcp-config"));
        Assert.Equal(string.Empty, After(arguments, "--setting-sources"));
        Assert.Equal("initialize", (string?)System.Text.Json.Nodes.JsonNode.Parse(await cli.Written.Reader.ReadAsync(Cancellation))!["request"]!["subtype"]);
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
        var folders = new ConfigurationFolders(home.Path);

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

    private static ClaudeCodeProvider Provider(FakeCli cli, string? home = null) =>
        new(cli, new ConfigurationFolders(home ?? Path.Combine(Path.GetTempPath(), "avala-no-home")));

    private static IAgentSession Started(Result<IAgentSession, AgentError> started) =>
        started.Match(session => session, error => throw new InvalidOperationException(error.ToString()));

    private static string? After(IReadOnlyList<string> arguments, string flag) =>
        arguments.SkipWhile(argument => argument != flag).Skip(1).FirstOrDefault();

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
