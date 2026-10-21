using System.Text.Json;
using System.Text.Json.Nodes;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.ClaudeCode.Replay;

public static class TranscriptPlayer
{
    public const string WorkingDirectory = "${workingDirectory}";

    public const string ConfigurationDirectory = "${configurationDirectory}";

    public const string EncodedWorkingDirectory = "${encodedWorkingDirectory}";

    public const string TemporaryDirectory = "${temporaryDirectory}";

    public const string MainRepository = "${mainRepository}";

    private const string CommandPrefix = "command:";

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
        var places = await PlacesAsync(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? string.Empty);
        transcript = [.. transcript.Select(entry => Placed(entry, places))];
        var position = 0;
        var results = new Dictionary<string, string>(StringComparer.Ordinal);

        while (position < transcript.Count)
        {
            if (transcript[position]["in"] is null)
            {
                await ActAsync(transcript[position], output, cwd, results);
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

                var message = JsonNode.Parse(received);
                Remember(message, results);
                var key = Key(message);
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

    private static async Task<Dictionary<string, string>> PlacesAsync(string login) => new(StringComparer.Ordinal)
    {
        [ConfigurationDirectory] = login,
        [EncodedWorkingDirectory] = string.Concat(Environment.CurrentDirectory.Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-')),
        [TemporaryDirectory] = Scratch(Environment.CurrentDirectory),
        [MainRepository] = await MainRepositoryAsync(Environment.CurrentDirectory),
    };

    public static JsonNode Placed(JsonNode node, IReadOnlyDictionary<string, string> places) => node switch
    {
        JsonObject members => new JsonObject(members.Select(member => KeyValuePair.Create(member.Key, member.Value is null ? null : Placed(member.Value, places)))),
        JsonArray items => new JsonArray([.. items.Select(item => item is null ? null : Placed(item, places))]),
        JsonValue value when value.TryGetValue<string>(out var text) => JsonValue.Create(PlacedText(text, places)),
        _ => node.DeepClone(),
    };

    private static string PlacedText(string text, IReadOnlyDictionary<string, string> places)
    {
        if (text.StartsWith('{') || text.StartsWith('['))
        {
            try
            {
                return JsonNode.Parse(text) is { } nested ? Placed(nested, places).ToJsonString() : text;
            }
            catch (JsonException)
            {
                return Filled(text, places);
            }
        }

        return Filled(text, places);
    }

    private static string Filled(string text, IReadOnlyDictionary<string, string> places) =>
        places.Aggregate(text, (filled, place) => filled.Replace(place.Key, place.Value, StringComparison.Ordinal));

    private static string Scratch(string workingDirectory) =>
        ScratchFolders.Of(new AvalaPaths(Path.GetDirectoryName(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(workingDirectory))) ?? workingDirectory), workingDirectory);

    private static async Task<string> MainRepositoryAsync(string workingDirectory)
    {
        var marker = Path.Combine(workingDirectory, ".git");

        if (!File.Exists(marker))
        {
            return workingDirectory;
        }

        var linked = (await File.ReadAllTextAsync(marker)).Trim();
        var administration = Path.GetFullPath(linked["gitdir:".Length..].Trim(), workingDirectory);
        var common = Path.GetDirectoryName(Path.GetDirectoryName(administration));

        return Path.GetDirectoryName(common) ?? workingDirectory;
    }

    private static void Remember(JsonNode? message, Dictionary<string, string> results)
    {
        if (message?["type"]?.GetValue<string>() == "user" && message["uuid"]?.GetValue<string>() is { } command)
        {
            results[$"{CommandPrefix}{results.Keys.Count(key => key.StartsWith(CommandPrefix, StringComparison.Ordinal)) + 1}"] = command;
        }

        var text = message?["response"]?["response"]?["mcp_response"]?["result"]?["content"]?[0]?["text"]?.GetValue<string>();

        if (text is null || !text.StartsWith('{'))
        {
            return;
        }

        try
        {
            Flatten(JsonNode.Parse(text), string.Empty, results);
        }
        catch (JsonException)
        {
        }
    }

    private static void Flatten(JsonNode? node, string path, Dictionary<string, string> results)
    {
        if (node is JsonObject members)
        {
            foreach (var (name, value) in members)
            {
                Flatten(value, path.Length == 0 ? name : $"{path}.{name}", results);
            }
        }
        else if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            results[path] = text;
        }
    }

    private static string Resolved(string line, Dictionary<string, string> results) =>
        results.Aggregate(line, (resolved, result) => resolved.Replace(
            result.Key.StartsWith(CommandPrefix, StringComparison.Ordinal) ? $"${{{result.Key}}}" : $"${{result:{result.Key}}}",
            result.Value,
            StringComparison.Ordinal));

    private static async Task ActAsync(JsonNode entry, TextWriter output, string cwd, Dictionary<string, string> results)
    {
        if (entry["out"] is { } line)
        {
            await output.WriteLineAsync(Resolved(line.ToJsonString().Replace(WorkingDirectory, cwd, StringComparison.Ordinal), results));
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
