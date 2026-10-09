using System.Diagnostics;

const string Branch = "refs/heads/badges";

if (args is not [var folder])
{
    await Console.Error.WriteLineAsync("Usage: dotnet run scripts/publish-badges.cs -- <badge-folder>");
    return 1;
}

if (Environment.GetEnvironmentVariable("GITHUB_EVENT_NAME") != "push" || Environment.GetEnvironmentVariable("GITHUB_REF") != "refs/heads/main")
{
    Console.WriteLine("Badges are published only from pushes to main.");
    return 0;
}

var badges = Directory.GetFiles(folder, "*.svg").Order(StringComparer.Ordinal).ToList();

if (badges.Count == 0)
{
    await Console.Error.WriteLineAsync($"No badges found in {folder}.");
    return 1;
}

var entries = new List<string>();

foreach (var badge in badges)
{
    entries.Add($"100644 blob {(await Git.RunAsync(["hash-object", "-w", badge])).Output}\t{Path.GetFileName(badge)}");
}

var tree = (await Git.RunAsync(["mktree"], string.Join('\n', entries) + "\n")).Output;
var published = (await Git.RunAsync(["ls-remote", "--exit-code", "origin", Branch], allowFailure: true)).Succeeded;
var parent = published ? await Git.FetchAsync(Branch) : null;

if (parent is not null && (await Git.RunAsync(["rev-parse", $"{parent}^{{tree}}"])).Output == tree)
{
    Console.WriteLine("The badges are unchanged.");
    return 0;
}

var source = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "main";
var commit = (await Git.RunAsync(["commit-tree", tree, "-m", $"Badges for {source}", .. parent is null ? Array.Empty<string>() : ["-p", parent]])).Output;
await Git.RunAsync(["push", "origin", $"{commit}:{Branch}"]);
Console.WriteLine($"Published {badges.Count} badges to {Branch}.");

return 0;

internal static class Git
{
    private const string Bot = "github-actions[bot]";
    private const string BotEmail = "41898282+github-actions[bot]@users.noreply.github.com";

    public static async Task<string> FetchAsync(string branch)
    {
        await RunAsync(["fetch", "--depth=1", "origin", branch]);

        return (await RunAsync(["rev-parse", "FETCH_HEAD"])).Output;
    }

    public static async Task<GitResult> RunAsync(IReadOnlyList<string> arguments, string? input = null, bool allowFailure = false)
    {
        var info = new ProcessStartInfo("git")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            Environment =
            {
                ["GIT_AUTHOR_NAME"] = Bot,
                ["GIT_AUTHOR_EMAIL"] = BotEmail,
                ["GIT_COMMITTER_NAME"] = Bot,
                ["GIT_COMMITTER_EMAIL"] = BotEmail,
            },
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("git could not start.");
        await process.StandardInput.WriteAsync(input ?? string.Empty);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var result = new GitResult(process.ExitCode == 0, (await output).Trim());

        return result.Succeeded || allowFailure
            ? result
            : throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {(await error).Trim()}");
    }
}

internal sealed record GitResult(bool Succeeded, string Output);
