using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.ClaudeCode.Conversations;
using Avala.ClaudeCode.Protocol;
using Avala.Sdk;

namespace Avala.ClaudeCode.Folders;

internal sealed class ConfigurationFolders(UserHome home) : IConfigurationFolders
{
    private const string GlobalConfiguration = ".claude.json";

    private const int MaximumBytes = 4 * 1024 * 1024;

    public string DefaultFolder => home.DefaultFolder;

    public async Task<Option<AgentAccount>> AccountAsync(ConnectionEnvironment connection, CancellationToken cancellationToken)
    {
        if (connection.ApiKey.IsSome)
        {
            return connection.ApiKey.Map(key => new AgentAccount($"api-key:{Fingerprint(key.Value)}", "Anthropic API key"));
        }

        var global = connection.ConfigurationDirectory
            .Bind(folder => home.IsDefault(folder) ? Option<string>.None : folder)
            .Match<string[]>(
                folder => [Path.Combine(folder, GlobalConfiguration)],
                () => [Path.Combine(home.Folder, GlobalConfiguration), Path.Combine(home.DefaultFolder, GlobalConfiguration)]);
        var account = new JsonObject();

        foreach (var candidate in global)
        {
            account = (await ReadAsync(candidate, cancellationToken)).Members("oauthAccount");

            if (account.Count > 0)
            {
                break;
            }
        }


        return account.Text("accountUuid").Map(id => new AgentAccount($"claude:{id}", account.TextOr("emailAddress", account.TextOr("displayName", id))));
    }

    public bool Holds(ConnectionEnvironment connection, Guid conversation) => Conversation(connection, conversation).IsSome;

    public async Task<IReadOnlyList<JsonNode>> EarlierAsync(ConnectionEnvironment connection, Guid conversation, CancellationToken cancellationToken) =>
        await Conversation(connection, conversation).Match(
            path => ToolMessagesAsync(path, cancellationToken),
            () => Task.FromResult<IReadOnlyList<JsonNode>>([]));

    private static async Task<IReadOnlyList<JsonNode>> ToolMessagesAsync(string path, CancellationToken cancellationToken)
    {
        var earlier = new List<JsonNode>();

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous);
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                earlier.AddRange(line.Contains("\"tool_use\"", StringComparison.Ordinal) || line.Contains("\"tool_result\"", StringComparison.Ordinal)
                    ? Parsed(line).Match<JsonNode[]>(message => [message], () => [])
                    : []);
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        return earlier;
    }

    private Option<string> Conversation(ConnectionEnvironment connection, Guid conversation)
    {
        var projects = Path.Combine(connection.ConfigurationDirectory.Match(folder => folder, () => DefaultFolder), "projects");
        var file = $"{conversation:D}.jsonl";

        try
        {
            return Directory.Exists(projects)
                ? Directory.EnumerateDirectories(projects).Select(project => Path.Combine(project, file)).FirstOrDefault(File.Exists).ToOption()
                : Option<string>.None;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return Option<string>.None;
        }
    }

    private static Option<JsonNode> Parsed(string line)
    {
        try
        {
            return JsonNode.Parse(line).ToOption();
        }
        catch (JsonException)
        {
            return Option<JsonNode>.None;
        }
    }

    private static async Task<JsonNode> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaximumBytes)
            {
                return new JsonObject();
            }

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous);

            return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken) ?? new JsonObject();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException)
        {
            return new JsonObject();
        }
    }

    private static string Fingerprint(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
}
