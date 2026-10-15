using System.Collections.Immutable;
using System.Globalization;
using Avala.Sdk.Processes;
using Avala.Workspaces.Contracts;

namespace Avala.Workspaces.Git;

internal sealed class GitIndex(IProcessRunner processes) : IRepositoryIndex
{
    private static readonly ImmutableDictionary<string, string> Unprompted =
        ImmutableDictionary<string, string>.Empty.Add("GIT_TERMINAL_PROMPT", "0");

    public async ValueTask<IReadOnlyList<string>> FilesAsync(string folder, int limit, CancellationToken cancellationToken)
    {
        var listed = await RunAsync(["-C", folder, "ls-files", "--cached", "--others", "--exclude-standard"], cancellationToken);

        return [.. Lines(listed).Take(limit)];
    }

    public async ValueTask<IReadOnlyList<CommitSummary>> CommitsAsync(string folder, int count, CancellationToken cancellationToken)
    {
        var logged = await RunAsync(["-C", folder, "log", string.Create(CultureInfo.InvariantCulture, $"-{count}"), "--format=%h%x09%s"], cancellationToken);

        return [.. Lines(logged).Select(line => line.Split('\t', 2)).Where(parts => parts.Length == 2).Select(parts => new CommitSummary(parts[0], parts[1]))];
    }

    private static string[] Lines(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private async Task<string> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        (await processes.RunAsync(new ProcessRequest("git", arguments) { Environment = Unprompted }, cancellationToken))
            .Match(outcome => outcome.Succeeded ? outcome.Output : string.Empty, _ => string.Empty);
}
