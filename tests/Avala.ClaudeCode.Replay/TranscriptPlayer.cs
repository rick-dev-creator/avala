using System.Text.Json;
using System.Text.Json.Nodes;

namespace Avala.ClaudeCode.Replay;

public static class TranscriptPlayer
{
    public const string WorkingDirectory = "${workingDirectory}";

    public const string ConfigurationDirectory = "${configurationDirectory}";

    public static async Task<int> PlayAsync(string[] args, TextReader input, TextWriter output, TextWriter error)
    {
        var resume = args.SkipWhile(argument => argument != "--resume").Skip(1).FirstOrDefault();
        var folder = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } configuration
            ? Path.GetFileName(Path.TrimEndingDirectorySeparator(configuration))
            : null;
        var transcript = await ChooseAsync(args[0], resume, folder);

        if (transcript is null)
        {
            await error.WriteLineAsync($"No conversation found with session ID: {resume}");

            return 1;
        }

        var cwd = JsonSerializer.Serialize(Environment.CurrentDirectory)[1..^1];
        var login = JsonSerializer.Serialize(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? string.Empty)[1..^1];
        transcript = [.. transcript.Select(entry => JsonNode.Parse(entry.ToJsonString().Replace(ConfigurationDirectory, login, StringComparison.Ordinal))!)];
        var position = 0;

        while (position < transcript.Count)
        {
            if (transcript[position]["in"] is null)
            {
                await ActAsync(transcript[position], output, cwd);
                position++;
                continue;
            }

            var expected = transcript.Skip(position).TakeWhile(entry => entry["in"] is not null).Select(entry => entry["in"]!).ToList();
            position += expected.Count;

            while (expected.Count > 0)
            {
                if (await input.ReadLineAsync() is not { } received)
                {
                    return 0;
                }

                var key = Key(JsonNode.Parse(received));
                var match = expected.FindIndex(candidate => Key(candidate) == key);

                if (match < 0)
                {
                    await error.WriteLineAsync($"Replay diverged: expected one of [{string.Join(", ", expected.Select(Key))}] but received {key}.");

                    return 3;
                }

                expected.RemoveAt(match);
            }
        }

        while (await input.ReadLineAsync() is not null)
        {
        }

        return 0;
    }

    private static async Task ActAsync(JsonNode entry, TextWriter output, string cwd)
    {
        if (entry["out"] is { } line)
        {
            await output.WriteLineAsync(line.ToJsonString().Replace(WorkingDirectory, cwd, StringComparison.Ordinal));
            await output.FlushAsync();
        }

        if (entry["file"] is { } file)
        {
            var path = Path.Combine(Environment.CurrentDirectory, file["path"]!.GetValue<string>());
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, file["content"]!.GetValue<string>());
        }
    }

    public static string Key(JsonNode? message)
    {
        var type = message?["type"]?.GetValue<string>() ?? "unknown";

        return type switch
        {
            "control_request" => $"request {message?["request"]?["subtype"]?.GetValue<string>()}",
            "control_response" => $"response {message?["response"]?["request_id"]?.GetValue<string>()}{Behavior(message?["response"]?["response"]?["mcp_response"]?["result"])}",
            _ => type,
        };
    }

    private static string Behavior(JsonNode? result)
    {
        var text = result?["content"]?[0]?["text"]?.GetValue<string>();

        if (text is null || !text.StartsWith('{'))
        {
            return string.Empty;
        }

        try
        {
            return JsonNode.Parse(text)?["behavior"]?.GetValue<string>() is { } behavior ? $" {behavior}" : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static async Task<List<JsonNode>?> ChooseAsync(string folder, string? resume, string? configuration)
    {
        foreach (var path in Directory.EnumerateFiles(folder, "*.jsonl").Order(StringComparer.Ordinal))
        {
            var lines = (await File.ReadAllLinesAsync(path)).Where(line => line.Length > 0).Select(line => JsonNode.Parse(line)!).ToList();
            var header = lines[0];
            var recordedResume = header["resume"]?.GetValue<string>();
            var recordedFolder = header["folder"]?.GetValue<string>();

            if (recordedResume == resume && (recordedFolder is null || configuration is null || recordedFolder == configuration))
            {
                return lines[1..];
            }
        }

        return null;
    }
}
