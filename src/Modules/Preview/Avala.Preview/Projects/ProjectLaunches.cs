using System.Text.Json;
using Avala.Preview.Running;
using Avala.Sdk;

namespace Avala.Preview.Projects;

internal sealed class ProjectLaunches : IProjectLaunches
{
    public const string Page = "index.html";

    private const long MaximumBytes = 256 * 1024;

    private static readonly string[] Scripts = ["dev", "start"];

    private static readonly (string LockFile, string Tool)[] Tools = [("pnpm-lock.yaml", "pnpm"), ("yarn.lock", "yarn"), ("bun.lockb", "bun"), ("bun.lock", "bun")];

    public async ValueTask<Option<Launch>> FindAsync(string worktree, CancellationToken cancellationToken)
    {
        return await ScriptsAsync(Path.Combine(worktree, "package.json"), cancellationToken) is [var script, ..]
            ? new Launch(script, LaunchKind.Script, Runner(worktree), ["run", script])
            : File.Exists(Path.Combine(worktree, Page))
                ? new Launch(Page, LaunchKind.Page, string.Empty, [])
                : Option<Launch>.None;
    }

    private static string Runner(string worktree)
    {
        var tool = Tools.FirstOrDefault(known => File.Exists(Path.Combine(worktree, known.LockFile))).Tool ?? "npm";

        return OperatingSystem.IsWindows() ? $"{tool}.cmd" : tool;
    }

    private static async Task<IReadOnlyList<string>> ScriptsAsync(string manifest, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(manifest) || new FileInfo(manifest).Length > MaximumBytes)
            {
                return [];
            }

            await using var stream = new FileStream(manifest, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("scripts", out var scripts)
                && scripts.ValueKind == JsonValueKind.Object
                    ? [.. Scripts.Where(name => scripts.TryGetProperty(name, out var command) && command.ValueKind == JsonValueKind.String)]
                    : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }
}
