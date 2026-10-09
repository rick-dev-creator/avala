using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Connections;
using Avala.ClaudeCode.Cli;
using Avala.ClaudeCode.Conversations;
using Avala.ClaudeCode.Protocol;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;

namespace Avala.ClaudeCode.Tests.Conversations;

public sealed class UserConfigurationTests
{
    private const string Gate = "AVALA_REAL_CLAUDE";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EachConnectionLoadsTheMemorySkillsAndMcpServersOfItsOwnFolderOnlyAsync()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(Gate) == "1", $"Set {Gate}=1 to start the real Claude Code CLI.");
        using var temporary = new TemporaryFolder();
        var home = temporary.Path;
        var defaultFolder = Path.Combine(home, ".claude");
        var workFolder = Path.Combine(home, ".claude-work");
        var repository = Directory.CreateDirectory(Path.Combine(home, "projects", "repository")).FullName;
        await LoginAsync(defaultFolder, Path.Combine(home, ".claude.json"), "default");
        await LoginAsync(workFolder, Path.Combine(workFolder, ".claude.json"), "work");
        await File.WriteAllTextAsync(Path.Combine(repository, "CLAUDE.md"), "# Repository", Cancellation);

        var onDefault = await LoadedAsync(home, repository, new ConnectionEnvironment { ConfigurationDirectory = defaultFolder });
        var onWork = await LoadedAsync(home, repository, new ConnectionEnvironment { ConfigurationDirectory = workFolder });
        var isolated = await LoadedAsync(home, repository, new ConnectionEnvironment
        {
            ConfigurationDirectory = workFolder,
            Settings = new Dictionary<string, string> { [CommandLine.UserConfigurationSetting] = "false" },
        });

        Assert.Equal(new Loaded([Path.Combine(defaultFolder, "CLAUDE.md"), Path.Combine(repository, "CLAUDE.md")], ["default-skill"], ["default-server"]), onDefault);
        Assert.Equal(new Loaded([Path.Combine(workFolder, "CLAUDE.md"), Path.Combine(repository, "CLAUDE.md")], ["work-skill"], ["work-server"]), onWork);
        Assert.Equal(new Loaded([], [], []), isolated);
    }

    private static async Task LoginAsync(string folder, string global, string name)
    {
        Directory.CreateDirectory(Path.Combine(folder, "skills", $"{name}-skill"));
        await File.WriteAllTextAsync(Path.Combine(folder, "CLAUDE.md"), $"# The {name} login", Cancellation);
        await File.WriteAllTextAsync(
            Path.Combine(folder, "skills", $"{name}-skill", "SKILL.md"),
            $"---\nname: {name}-skill\ndescription: The {name} skill.\n---\nSay {name}.\n",
            Cancellation);
        await File.WriteAllTextAsync(
            global,
            new JsonObject { ["mcpServers"] = new JsonObject { [$"{name}-server"] = new JsonObject { ["type"] = "stdio", ["command"] = "sleep", ["args"] = new JsonArray("600") } } }.ToJsonString(),
            Cancellation);
    }

    private static async Task<Loaded> LoadedAsync(string home, string repository, ConnectionEnvironment connection)
    {
        var launch = CommandLine.For(repository, connection, Option<ConversationMark>.None, new UserHome(home, []));
        launch = launch with { Variables = new Dictionary<string, string>(launch.Variables) { ["HOME"] = home } };
        var cli = new ProcessCli(CliCommand.Installed, TimeProvider.System).Start(launch, UncontainedProcesses.Instance, Option<string>.None)
            .Match(started => started, error => throw new InvalidOperationException(error.ToString()));
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        guard.CancelAfter(TimeSpan.FromMinutes(1));
        JsonNode? context = null;
        JsonNode? servers = null;

        await using (cli)
        {
            await cli.WriteAsync(Messages.Initialize().ToJsonString(), Cancellation);
            await cli.WriteAsync("""{"type":"control_request","request_id":"context","request":{"subtype":"get_context_usage"}}""", Cancellation);
            await cli.WriteAsync("""{"type":"control_request","request_id":"servers","request":{"subtype":"mcp_status"}}""", Cancellation);

            await foreach (var line in cli.Lines.WithCancellation(guard.Token))
            {
                var response = JsonNode.Parse(line)?["response"];
                context = response?["request_id"]?.GetValue<string>() == "context" ? response["response"] : context;
                servers = response?["request_id"]?.GetValue<string>() == "servers" ? response["response"] : servers;

                if (context is not null && servers is not null)
                {
                    break;
                }
            }
        }

        return new Loaded(
            [.. Items(context, "memoryFiles").Select(file => file["path"]!.GetValue<string>())],
            [.. Items(context?["skills"], "skillFrontmatter").Where(skill => skill["source"]?.GetValue<string>() != "bundled" && skill["name"]!.GetValue<string>().EndsWith("-skill", StringComparison.Ordinal)).Select(skill => skill["name"]!.GetValue<string>())],
            [.. Items(servers, "mcpServers").Select(server => server["name"]!.GetValue<string>()).Order(StringComparer.Ordinal)]);
    }

    private static IEnumerable<JsonNode> Items(JsonNode? node, string name) => node?[name]?.AsArray().OfType<JsonNode>() ?? [];

    private sealed record Loaded(IReadOnlyList<string> Memory, IReadOnlyList<string> Skills, IReadOnlyList<string> Servers)
    {
        public bool Equals(Loaded? other) =>
            other is not null && Memory.SequenceEqual(other.Memory) && Skills.SequenceEqual(other.Skills) && Servers.SequenceEqual(other.Servers);

        public override int GetHashCode() => Memory.Count;

        public override string ToString() => $"memory [{string.Join(", ", Memory)}], skills [{string.Join(", ", Skills)}], servers [{string.Join(", ", Servers)}]";
    }
}
