using System.Text.Json;
using System.Text.Json.Nodes;
using Avala.ClaudeCode;
using Avala.ClaudeCode.Replay;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

internal sealed class TranscribedClaude : IDisposable
{
    public const string Connection = "claude";

    public const string Simulator = "sim";

    private const string Login = ".claude-work";

    private readonly TemporaryFolder home = new();

    private TranscribedClaude(string transcripts) => Transcripts = transcripts;

    public string Transcripts { get; }

    public IPlugin Plugin => new ClaudeCodePlugin("dotnet", [typeof(TranscriptPlayer).Assembly.Location, Transcripts], home.Path, []);

    public IReadOnlyList<(string File, string Content)> Data =>
    [
        ("connections.json", new JsonObject
        {
            ["default"] = Connection,
            ["connections"] = new JsonArray(
                new JsonObject
                {
                    ["name"] = Connection,
                    ["provider"] = "claude-code",
                    ["credential"] = new JsonObject { ["source"] = "login", ["reference"] = Path.Combine(home.Path, Login) },
                },
                new JsonObject { ["name"] = Simulator, ["provider"] = "simulator", ["credential"] = new JsonObject { ["source"] = "login" } }),
        }.ToJsonString()),
        ($"connections/{Simulator}/.login", string.Empty),
        ("recording.json", new JsonObject { ["enabled"] = true, ["redact"] = new JsonArray(home.Path) }.ToJsonString()),
    ];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static async Task<TranscribedClaude> PrepareAsync(string name)
    {
        var claude = new TranscribedClaude(Path.Combine(Repository.Root.FullName, "tests", "transcripts", "claude-code", name));
        var projects = Directory.CreateDirectory(Path.Combine(claude.home.Path, Login, "projects", "replayed")).FullName;
        await File.WriteAllTextAsync(
            Path.Combine(claude.home.Path, Login, ".claude.json"),
            """{ "oauthAccount": { "accountUuid": "account-work", "emailAddress": "work@example.com" } }""",
            Cancellation);

        foreach (var transcript in Directory.EnumerateFiles(claude.Transcripts, "*.jsonl"))
        {
            if (JsonNode.Parse((await File.ReadAllLinesAsync(transcript, Cancellation))[0])?["resume"]?.GetValue<string>() is { } resumed)
            {
                await File.WriteAllTextAsync(Path.Combine(projects, $"{resumed}.jsonl"), "{}", Cancellation);
            }
        }

        return claude;
    }

    public static string Comparable(string recording)
    {
        var parsed = JsonNode.Parse(recording)!.AsObject();
        parsed.Remove("recordedAt");

        foreach (var result in (parsed["entries"]?.AsArray() ?? []).Select(entry => entry?["return"] ?? entry?["event"]?["result"]).OfType<JsonObject>())
        {
            result["content"] = "The harness's own result, which names the jobs it ran.";
        }

        return parsed.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public void Dispose() => home.Dispose();
}
