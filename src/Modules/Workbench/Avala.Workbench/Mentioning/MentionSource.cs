using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Reviewing;
using Avala.Workbench.Timeline;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Mentioning;

internal enum MentionKind
{
    File,
    Folder,
    Job,
    Commit,
}

internal sealed record Mention(MentionKind Kind, string Token, string Label, string Detail, string Reference);

internal sealed class MentionSource(WorkedFiles worked, IRepositoryIndex index, JobBoard board, TimeProvider clock)
{
    public const int Shown = 8;

    public const int FilesListed = 5000;

    public const int CommitsListed = 30;

    public const int ReplyBrief = 400;

    public const int TitleLength = 80;

    private static readonly TimeSpan Kept = TimeSpan.FromSeconds(30);

    private ImmutableDictionary<string, (DateTimeOffset At, IReadOnlyList<Mention> Mentions)> listed = ImmutableDictionary<string, (DateTimeOffset, IReadOnlyList<Mention>)>.Empty;

    public async Task<IReadOnlyList<Mention>> SuggestAsync(JobId job, string query, CancellationToken cancellationToken)
    {
        var known = await worked.FolderAsync(job, cancellationToken);
        var places = await known.Match(folder => PlacesAsync(folder, cancellationToken), () => Task.FromResult<IReadOnlyList<Mention>>([]));
        var all = places.Concat(Jobs(job));

        return
        [
            .. all.Where(mention => mention.Label.Contains(query, StringComparison.OrdinalIgnoreCase) || (mention.Kind == MentionKind.Commit && mention.Detail.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(mention => mention.Label.StartsWith(query, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(mention.Label).StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(mention => mention.Label.Length)
                .Take(Shown),
        ];
    }

    private async Task<IReadOnlyList<Mention>> PlacesAsync(string folder, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        if (Volatile.Read(ref listed).TryGetValue(folder, out var cached) && now - cached.At < Kept)
        {
            return cached.Mentions;
        }

        var files = await index.FilesAsync(folder, FilesListed, cancellationToken);
        var commits = await index.CommitsAsync(folder, CommitsListed, cancellationToken);
        IReadOnlyList<Mention> found =
        [
            .. files.Select(file => new Mention(MentionKind.File, file, file, "file", $"the file {file} in the job's worktree")),
            .. files.Select(Path.GetDirectoryName).OfType<string>().Where(directory => directory.Length > 0).Select(directory => directory.Replace('\\', '/')).Distinct(StringComparer.Ordinal)
                .Select(directory => new Mention(MentionKind.Folder, $"{directory}/", $"{directory}/", "folder", $"the folder {directory}/ in the job's worktree")),
            .. commits.Select(commit => new Mention(MentionKind.Commit, commit.Sha, commit.Sha, commit.Subject, $"commit {commit.Sha}, \"{commit.Subject}\"")),
        ];
        ImmutableInterlocked.Update(ref listed, current => current.SetItem(folder, (now, found)));

        return found;
    }

    private IEnumerable<Mention> Jobs(JobId current) =>
        board.Jobs.Values
            .Where(other => other.Job != current)
            .OrderByDescending(other => other.Summary.Submitted)
            .Select(other => new Mention(MentionKind.Job, Title(other.Summary.Instruction), Title(other.Summary.Instruction), "job", Brief(other)));

    private static string Brief(BoardJob job)
    {
        var reply = job.Transcript.Entries.OfType<MessageEntry>().LastOrDefault()?.Text ?? string.Empty;
        var said = reply.Length <= ReplyBrief ? reply : $"{reply[..ReplyBrief]}…";

        return said.Length == 0
            ? $"another job, \"{Title(job.Summary.Instruction)}\", {job.Status}: {job.Summary.Instruction}"
            : $"another job, \"{Title(job.Summary.Instruction)}\", {job.Status}: {job.Summary.Instruction}\nIts last reply: {said}";
    }

    private static string Title(string instruction)
    {
        var line = instruction.Trim().Split('\n', 2)[0].Trim();

        return line.Length <= TitleLength ? line : $"{line[..(TitleLength - 1)].TrimEnd()}…";
    }
}
