using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.ClaudeCode.Protocol;
using Avala.Sdk;

namespace Avala.ClaudeCode.Conversations;

internal sealed record CliCommand(string FileName, IReadOnlyList<string> Prefix)
{
    public static CliCommand Installed { get; } = new("claude", []);
}

internal sealed record CliLaunch(
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<string> Cleared,
    IReadOnlyDictionary<string, string> Variables);

internal static class CommandLine
{
    public const string Server = "avala";

    public const string PermissionTool = "permission_prompt";

    public const string TranscriptsSetting = "transcripts";

    public const string UserConfigurationSetting = "userConfiguration";

    public const string UserHooksSetting = "userHooks";

    public const string ConfigurationVariable = "CLAUDE_CONFIG_DIR";

    public const string KeyVariable = "ANTHROPIC_API_KEY";

    private static readonly string[] Inherited =
    [
        ConfigurationVariable, KeyVariable, "ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN",
        "CLAUDECODE", "CLAUDE_CODE_ENTRYPOINT", "CLAUDE_CODE_SESSION_ID", "CLAUDE_CODE_CHILD_SESSION", "CLAUDE_CODE_BRIDGE_SESSION_ID",
        "CLAUDE_CODE_MESSAGING_SOCKET", "CLAUDE_CODE_MESSAGING_TOKEN", "CLAUDE_CODE_SESSION_ATTENDED", "CLAUDE_CODE_EXECPATH",
        "CLAUDE_CODE_SSE_PORT", "CLAUDE_PID", "CLAUDE_EFFORT", "CLAUDE_AUTO_BACKGROUND_TASKS", "CLAUDE_CODE_MCP_AUTO_BACKGROUND_MS",
    ];

    public const string ToolTimeoutVariable = "MCP_TOOL_TIMEOUT";

    public const int ToolTimeoutMilliseconds = int.MaxValue;

    public static string Qualified(string tool) => $"mcp__{Server}__{tool}";

    public static Option<string> Unqualified(string tool) =>
        tool.StartsWith($"mcp__{Server}__", StringComparison.Ordinal) ? tool[$"mcp__{Server}__".Length..] : Option<string>.None;

    public static CliLaunch For(string workingDirectory, SessionOptions options, Option<ConversationMark> resume, UserHome home) =>
        new(workingDirectory, Arguments(options, resume, home), Inherited, Variables(options.Connection, home));

    private static List<string> Arguments(SessionOptions options, Option<ConversationMark> resume, UserHome home)
    {
        var connection = options.Connection;
        var model = ClaudeModels.Chosen(options);
        var servers = new JsonObject
        {
            ["mcpServers"] = new JsonObject
            {
                [Server] = new JsonObject { ["type"] = "sdk", ["name"] = Server, ["timeout"] = ToolTimeoutMilliseconds, ["disableAutoBackground"] = true },
            },
        };

        return
        [
            "-p",
            "--input-format", "stream-json",
            "--output-format", "stream-json",
            "--verbose",
            "--include-partial-messages",
            "--forward-subagent-text",
            "--permission-mode", "default",
            "--permission-prompt-tool", Qualified(PermissionTool),
            "--mcp-config", servers.ToJsonString(),
            .. Configuration(connection, home),
            .. Flag(model.Model, "--model"),
            .. Flag(model.Effort, "--effort"),
            .. resume.Match<string[]>(mark => ["--resume", mark.Session.ToString("D")], () => []),
        ];
    }

    private static Dictionary<string, string> Variables(ConnectionEnvironment connection, UserHome home)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DISABLE_AUTOUPDATER"] = "1",
            [ToolTimeoutVariable] = ToolTimeoutMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        foreach (var folder in connection.ConfigurationDirectory.Match<string[]>(folder => home.IsDefault(folder) ? [] : [folder], () => []))
        {
            environment[ConfigurationVariable] = folder;
        }

        foreach (var key in connection.ApiKey.Match<Secret[]>(key => [key], () => []))
        {
            environment[KeyVariable] = key.Value;
        }

        return environment;
    }

    private static string[] Configuration(ConnectionEnvironment connection, UserHome home)
    {
        if (!Switch(connection, UserConfigurationSetting, true) || connection.ApiKey.IsSome && connection.ConfigurationDirectory.IsNone)
        {
            return ["--strict-mcp-config", "--setting-sources", string.Empty];
        }

        var settings = new JsonObject();

        if (!Switch(connection, UserHooksSetting, false))
        {
            settings["disableAllHooks"] = true;
        }

        if (!connection.ConfigurationDirectory.Match(home.IsDefault, () => true))
        {
            settings["claudeMdExcludes"] = new JsonArray($"{home.DefaultFolder.Replace('\\', '/')}/**");
        }

        return ["--setting-sources", "user,project,local", .. settings.Count > 0 ? ["--settings", settings.ToJsonString()] : Array.Empty<string>()];
    }

    private static bool Switch(ConnectionEnvironment connection, string name, bool fallback) =>
        connection.Settings.TryGetValue(name, out var value) && bool.TryParse(value, out var on) ? on : fallback;

    private static string[] Flag(Option<string> value, string flag) => value.Match<string[]>(set => [flag, set], () => []);
}
