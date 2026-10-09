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

internal sealed class ConfigurationFolders(string home) : IConfigurationFolders
{
    private const string GlobalConfiguration = ".claude.json";

    private const int MaximumBytes = 4 * 1024 * 1024;

    public ConfigurationFolders()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    {
    }

    public string DefaultFolder => Path.Combine(home, ".claude");

    public async Task<Option<AgentAccount>> AccountAsync(ConnectionEnvironment connection, CancellationToken cancellationToken)
    {
        if (connection.ApiKey.IsSome)
        {
            return connection.ApiKey.Map(key => new AgentAccount($"api-key:{Fingerprint(key.Value)}", "Anthropic API key"));
        }

        var global = connection.ConfigurationDirectory.Match(folder => Path.Combine(folder, GlobalConfiguration), () => Path.Combine(home, GlobalConfiguration));
        var account = (await ReadAsync(global, cancellationToken)).Members("oauthAccount");

        return account.Text("accountUuid").Map(id => new AgentAccount($"claude:{id}", account.TextOr("emailAddress", account.TextOr("displayName", id))));
    }

    public bool Holds(ConnectionEnvironment connection, Guid conversation)
    {
        var projects = Path.Combine(connection.ConfigurationDirectory.Match(folder => folder, () => DefaultFolder), "projects");
        var file = $"{conversation:D}.jsonl";

        try
        {
            return Directory.Exists(projects) && Directory.EnumerateDirectories(projects).Any(project => File.Exists(Path.Combine(project, file)));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task<JsonNode?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaximumBytes)
            {
                return null;
            }

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous);

            return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static string Fingerprint(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
}
