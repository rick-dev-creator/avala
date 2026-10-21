using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

if (args is not [var tap, var name, var recorded])
{
    await Console.Error.WriteLineAsync("Usage: dotnet run scripts/anonymize-transcript.cs -- <tapped-session.jsonl> <fixture-name> <what-the-session-shows>");
    return 1;
}

var lines = (await File.ReadAllLinesAsync(tap)).Where(line => line.Length > 0).Select(line => JsonNode.Parse(line)!).ToList();
var header = lines[0].AsObject();
var init = lines.Select(line => line["out"]).FirstOrDefault(output => output?["type"]?.GetValue<string>() == "system" && output["subtype"]?.GetValue<string>() == "init");

if (init is null)
{
    await Console.Error.WriteLineAsync($"{tap} holds no system init message, so its working folder is unknown.");
    return 1;
}

var account = lines.Select(line => line["out"]?["response"]?["response"]?["account"] ?? line["out"]?["account"]).FirstOrDefault(found => found is not null);
var replacements = Anonymizer.Replacements(header, init, account);
var anonymized = lines.Skip(1).Select(line => Anonymizer.Clean(line, replacements)).ToList();
Anonymizer.Replace(header, replacements);
var folder = Path.Combine(Repository.Root().FullName, "tests", "transcripts", "claude-code", name);
Directory.CreateDirectory(folder);
header["folder"] = Anonymizer.Login;
header.Remove("crafted");
header["recorded"] = recorded;
var output = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(tap)[..16]}-recorded-{name}.jsonl");
await File.WriteAllLinesAsync(output, [header.ToJsonString(), .. anonymized]);
Console.WriteLine(output);

return 0;

internal static partial class Anonymizer
{
    public const string Login = ".claude-work";

    private const string WorkingDirectory = "${workingDirectory}";

    private const string ConfigurationDirectory = "${configurationDirectory}";

    private static readonly string[] Personal = ["slash_commands", "terminal_slash_commands", "skills", "plugins", "agents"];

    public static IReadOnlyList<(string From, string To)> Replacements(JsonObject header, JsonNode init, JsonNode? account)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var configuration = Path.Combine(home, header["folder"]!.GetValue<string>());
        var cwd = init["cwd"]!.GetValue<string>();
        var data = Path.GetDirectoryName(Path.GetDirectoryName(cwd)) ?? cwd;

        return
        [
            (cwd, WorkingDirectory),
            (configuration, ConfigurationDirectory),
            .. new[] { account?["email"]?.GetValue<string>(), account?["organization"]?.GetValue<string>() }
                .OfType<string>()
                .Select((value, index) => (value, index == 0 ? "work@example.com" : "Example Organization")),
            (data, "${dataDirectory}"),
            (home, "/home/user"),
        ];
    }

    public static string Clean(JsonNode line, IReadOnlyList<(string From, string To)> replacements)
    {
        if (line["out"] is JsonObject { } output && output["type"]?.GetValue<string>() == "system" && output["subtype"]?.GetValue<string>() == "init")
        {
            foreach (var field in Personal.Where(output.ContainsKey))
            {
                output[field] = new JsonArray();
            }

            output["mcp_servers"] = new JsonArray(new JsonObject { ["name"] = "avala", ["status"] = "connected", ["source"] = "sdk" });
            output["messaging_socket_path"] = string.Empty;
        }

        if (line["out"]?["response"]?["response"] is JsonObject { } answered && answered.ContainsKey("commands"))
        {
            answered["commands"] = new JsonArray();
            answered["agents"] = new JsonArray();
        }

        Replace(line, replacements);

        return line.ToJsonString();
    }

    public static void Replace(JsonNode node, IReadOnlyList<(string From, string To)> replacements)
    {
        switch (node)
        {
            case JsonObject value:
                foreach (var (key, child) in value.ToList())
                {
                    Visit(child, replacements, replaced => value[key] = replaced);
                }

                break;
            case JsonArray items:
                for (var index = 0; index < items.Count; index++)
                {
                    var at = index;
                    Visit(items[at], replacements, replaced => items[at] = replaced);
                }

                break;
        }
    }

    private static void Visit(JsonNode? child, IReadOnlyList<(string From, string To)> replacements, Action<JsonNode> replace)
    {
        if (child is JsonValue value && value.TryGetValue<string>(out var text))
        {
            var cleaned = ProjectFolder().Replace(replacements.Aggregate(text, (current, replacement) => current.Replace(replacement.From, replacement.To, StringComparison.Ordinal)), $"{ConfigurationDirectory}/projects/replayed");
            replace(JsonValue.Create(cleaned));
        }
        else if (child is not null)
        {
            Replace(child, replacements);
        }
    }

    [GeneratedRegex(@"\$\{configurationDirectory\}/projects/[^/""\\\s]+")]
    private static partial Regex ProjectFolder();
}

internal static class Repository
{
    public static DirectoryInfo Root()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Avala.slnx")))
        {
            directory = directory.Parent;
        }

        return directory ?? throw new InvalidOperationException("Run the script inside the Avala repository.");
    }
}
