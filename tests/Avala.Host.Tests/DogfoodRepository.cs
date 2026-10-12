using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Avala.Host.Tests.DogfoodSettings;

namespace Avala.Host.Tests;

internal static class DogfoodRepository
{
    public const string Calculator = "module.exports = { add: (a, b) => a + b };\n";

    public const string Checks = """{ "checks": [ { "name": "tests", "command": "node", "arguments": ["--test"], "timeoutSeconds": 60 } ] }""";

    public const string Jobs = $$"""{ "connection": "{{Main}}", "approval": "keep" }""";

    public static string Permissions(string autonomy, params string[] rules) => $$"""
        {
          "autonomy": "{{autonomy}}",
          "formAnswers": "recommended",
          "rules": [
            {{string.Join(",\n    ", [.. rules, .. BaseRules])}}
          ]
        }
        """;

    public static string Budget(decimal cap) => $$"""{ "costPerJob": { "USD": {{Money(cap)}} } }""";

    public static string Money(decimal amount) => amount.ToString(CultureInfo.InvariantCulture);

    public static string Json(string text) => JsonSerializer.Serialize(text);

    private static readonly string[] BaseRules =
    [
        """{ "name": "run the tests", "kind": "command", "target": "node --test*", "answer": "allow" }""",
        """{ "name": "list files", "kind": "command", "target": "ls*", "answer": "allow" }""",
        """{ "name": "show files", "kind": "command", "target": "cat*", "answer": "allow" }""",
        """{ "name": "print text", "kind": "command", "target": "echo*", "answer": "allow" }""",
        """{ "name": "show the folder", "kind": "command", "target": "pwd*", "answer": "allow" }""",
        """{ "name": "show the start of files", "kind": "command", "target": "head*", "answer": "allow" }""",
        """{ "name": "count lines", "kind": "command", "target": "wc*", "answer": "allow" }""",
        """{ "name": "node version", "kind": "command", "target": "node --version*", "answer": "allow" }""",
        """{ "name": "git status", "kind": "command", "target": "git status*", "answer": "allow" }""",
        """{ "name": "git diff", "kind": "command", "target": "git diff*", "answer": "allow" }""",
        """{ "name": "git log", "kind": "command", "target": "git log*", "answer": "allow" }""",
        """{ "name": "no pushing", "kind": "command", "target": "git push*", "answer": "deny" }""",
        """{ "name": "no remotes", "kind": "command", "target": "git remote*", "answer": "deny" }""",
        """{ "name": "no curl", "kind": "command", "target": "curl*", "answer": "deny" }""",
        """{ "name": "no wget", "kind": "command", "target": "wget*", "answer": "deny" }""",
        """{ "name": "no installs", "kind": "command", "target": "npm install*", "answer": "deny" }""",
        """{ "name": "no npx", "kind": "command", "target": "npx*", "answer": "deny" }""",
        """{ "name": "no network", "kind": "web", "answer": "deny" }""",
        """{ "name": "no mcp", "kind": "mcp", "answer": "deny" }""",
    ];

    public static async Task PrepareAsync(DogfoodJournal journal, DogfoodScenario scenario, DogfoodSetup setup)
    {
        var place = setup.Place;
        var seed = scenario.Seed(setup);
        Directory.CreateDirectory(place.Repository);
        Directory.CreateDirectory(place.Data);
        DogfoodFile[] defaults =
        [
            new("calc.js", Calculator),
            new("README.md", "# calc\n"),
            new(".avala/permissions.json", Permissions("supervised")),
            new(".avala/checks.json", Checks),
            new(".avala/jobs.json", Jobs),
            new(".avala/budget.json", Budget(scenario.Cap)),
        ];
        var committed = defaults.Where(file => seed.Committed.All(chosen => chosen.Path != file.Path)).Concat(seed.Committed).Where(file => file.Content.Length > 0).ToList();
        await WriteAllAsync(place.Repository, committed);
        await GitAsync(place.Repository, "init", "--quiet", "--initial-branch=main");
        await CommitAsync(place, "Start calc with Avala's rules");

        if (seed.Origin)
        {
            Directory.CreateDirectory(place.Origin);
            await GitAsync(place.Origin, "init", "--quiet", "--bare", "--initial-branch=main");
            await GitAsync(place.Repository, "remote", "add", "origin", place.Origin);
            await GitAsync(place.Repository, "push", "--quiet", "origin", "main");
        }

        await AssertLocalAsync(journal, place);
        DogfoodFile[] data =
        [
            new("connections.json", Connections(scenario, setup)),
            new("budgets.json", $$"""{ "spendPerDay": { "USD": {{Money(scenario.Cap)}} } }"""),
            new("recording.json", """{ "enabled": true }"""),
            new("onboarding.json", """{ "onboarded": true }"""),
            .. seed.Recordings.Select(script => new DogfoodFile($"recordings/{script.Name}.json", script.Json)),
        ];
        await WriteAllAsync(place.Data, [.. data.Where(file => seed.Data.All(chosen => chosen.Path != file.Path)), .. seed.Data]);

        foreach (var login in setup.Rehearsal ? Declared(scenario).Select(declared => Path.GetFileName(declared.Login)).Distinct() : [])
        {
            await WriteAllAsync(place.Logins, [new DogfoodFile($"{login}/.login", string.Empty)]);
        }

        await journal.NoteAsync($"Prepared {place.Repository} with [{string.Join(", ", committed.Select(file => file.Path))}] and {place.Data} with [{string.Join(", ", data.Select(file => file.Path).Concat(seed.Data.Select(file => file.Path)).Distinct())}]");
    }

    public static async Task CommitAsync(DogfoodPlace place, string message)
    {
        await GitAsync(place.Repository, "add", "--all");
        await GitAsync(place.Repository, "-c", "user.name=Dogfood", "-c", "user.email=dogfood@localhost", "-c", "commit.gpgsign=false", "commit", "--quiet", "--allow-empty", "--message", message);
    }

    public static async Task CommitAsync(DogfoodPlace place, string path, string content, string message)
    {
        await WriteAllAsync(place.Repository, [new DogfoodFile(path, content)]);
        await CommitAsync(place, message);
    }

    public static async Task AssertLocalAsync(DogfoodJournal journal, DogfoodPlace place)
    {
        var remotes = (await GitAsync(place.Repository, "remote", "-v")).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var foreign = remotes.Where(line => !line.Replace('\t', ' ').Split(' ').Skip(1).First().StartsWith(place.Folder, StringComparison.Ordinal)).ToList();
        await journal.NoteAsync($"Remotes: [{string.Join(" | ", remotes)}]");
        Assert.True(foreign.Count == 0, $"The scenario repository has a remote outside its folder: {string.Join(" | ", foreign)}");
    }

    public static async Task<string> GitAsync(string directory, params string[] arguments)
    {
        var (code, output) = await RunAsync("git", ["-C", directory, .. arguments], new Dictionary<string, string?>());
        Assert.True(code == 0, $"git {string.Join(' ', arguments)} exited with {code}: {output}");

        return output;
    }

    public static async Task<(int Code, string Output)> RunAsync(string file, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string?> variables)
    {
        var start = new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };

        foreach (var (name, value) in variables)
        {
            start.Environment[name] = value;
        }

        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(Cancellation);
        var error = process.StandardError.ReadToEndAsync(Cancellation);
        await process.WaitForExitAsync(Cancellation);

        return (process.ExitCode, (await output) + (await error));
    }

    public static IReadOnlyList<(string Name, string Model, string Login)> Declared(DogfoodScenario scenario) =>
    [
        (Main, scenario.Model, MainLogin),
        .. scenario.LeadModel.Length > 0 ? [(Lead, scenario.LeadModel, MainLogin)] : Array.Empty<(string, string, string)>(),
        .. scenario.UsesExhausted ? [(Exhausted, scenario.Model, ExhaustedLogin)] : Array.Empty<(string, string, string)>(),
    ];

    private static string Connections(DogfoodScenario scenario, DogfoodSetup setup) =>
        new JsonObject
        {
            ["default"] = Main,
            ["connections"] = new JsonArray([.. Declared(scenario).Select(declared => Connection(declared, setup))]),
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    private static JsonObject Connection((string Name, string Model, string Login) declared, DogfoodSetup setup) =>
        setup.Rehearsal
            ? new JsonObject
            {
                ["name"] = declared.Name,
                ["provider"] = "simulator",
                ["credential"] = new JsonObject { ["source"] = "login", ["reference"] = Path.Combine(setup.Place.Logins, Path.GetFileName(declared.Login)) },
                ["settings"] = new JsonObject { ["transcripts"] = Path.Combine(setup.Place.Folder, "transcripts") },
            }
            : new JsonObject
            {
                ["name"] = declared.Name,
                ["provider"] = "claude-code",
                ["credential"] = new JsonObject { ["source"] = "login", ["reference"] = declared.Login },
                ["settings"] = new JsonObject { ["model"] = declared.Model, ["transcripts"] = Path.Combine(setup.Place.Folder, "transcripts"), ["userConfiguration"] = false },
            };

    private static async Task WriteAllAsync(string folder, IReadOnlyList<DogfoodFile> files)
    {
        foreach (var file in files)
        {
            var path = Path.Combine(folder, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, file.Content, Cancellation);
        }
    }
}
