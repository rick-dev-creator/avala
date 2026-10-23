using System.Security.Cryptography;
using System.Text;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Workspaces.Testing;

internal sealed record GitCommit(string Id, FolderSnapshot Files, string Message, Option<string> Parent);

internal sealed record HeldWorkspace(WorkspaceInfo Info, string Repository, IReadOnlyList<CheckpointInfo> Checkpoints, Option<string> LastMerged)
{
    public Option<string> Tip { get; init; }

    public string Head => Tip.Match(tip => tip, () => Info.BaseCommit);
}

public sealed class InMemoryGit
{
    public const string MainBranch = "main";

    private const string GitEntry = ".git";

    private const int CommitLength = 40;

    private static readonly StringComparison Comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly Lock gate = new();
    private readonly Dictionary<string, GitCommit> commits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> heads = new(StringComparer.Ordinal);
    private readonly Dictionary<WorkspaceId, HeldWorkspace> workspaces = [];
    private readonly Dictionary<(string Repository, string Remote), string> remotes = [];
    private int made;

    public Option<string> ProjectsFolder { get; set; }

    public static string Canonical(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));

    public async Task CreateAsync(string repository, IReadOnlyList<(string Path, string Content)> files, CancellationToken cancellationToken)
    {
        var folder = Canonical(repository);
        Directory.CreateDirectory(Path.Combine(folder, GitEntry));
        await FolderSnapshot.Of([.. files]).WriteToAsync(folder, cancellationToken);
        Advance(folder, Record(await FolderSnapshot.ReadAsync(folder, cancellationToken), "Initial commit", Option<string>.None).Id);
    }

    public async Task CommitAsync(string repository, string path, string content, CancellationToken cancellationToken)
    {
        var folder = Canonical(repository);
        var file = Path.Combine(folder, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, content, cancellationToken);
        var head = await HeadOfAsync(folder, cancellationToken);
        Move(folder, Record(await FolderSnapshot.ReadAsync(folder, cancellationToken), $"Add {path}", head.Id));
    }

    public void AddRemote(string repository, string remote, string url)
    {
        lock (gate)
        {
            remotes[(Canonical(repository), remote)] = url;
        }
    }

    public bool Holds(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return false;
        }

        var path = Canonical(folder);

        lock (gate)
        {
            return heads.Keys.Concat(workspaces.Values.Select(workspace => workspace.Info.Path)).Any(root => Inside(path, root));
        }
    }

    internal Option<string> RootOf(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Option<string>.None;
        }

        var folder = Canonical(path);

        lock (gate)
        {
            if (workspaces.Values.FirstOrDefault(workspace => Inside(folder, workspace.Info.Path)) is { } held)
            {
                return held.Info.Path;
            }
        }

        for (var current = new DirectoryInfo(folder); current is not null; current = current.Parent)
        {
            if (Directory.Exists(Path.Combine(current.FullName, GitEntry)) || File.Exists(Path.Combine(current.FullName, GitEntry)))
            {
                return Canonical(current.FullName);
            }
        }

        return Option<string>.None;
    }

    internal async Task<GitCommit> HeadOfAsync(string repository, CancellationToken cancellationToken)
    {
        if (Known(repository) is { } known)
        {
            return known;
        }

        var first = Record(await FolderSnapshot.ReadAsync(repository, cancellationToken), "Initial commit", Option<string>.None);

        lock (gate)
        {
            _ = heads.TryAdd(repository, first.Id);

            return commits[heads[repository]];
        }
    }

    internal Option<GitCommit> Find(string id)
    {
        lock (gate)
        {
            return commits.TryGetValue(id, out var found) ? found : Option<GitCommit>.None;
        }
    }

    internal async Task<Option<GitCommit>> ResolveAsync(string repository, string reference, CancellationToken cancellationToken)
    {
        if (reference is "HEAD" or MainBranch)
        {
            return await HeadOfAsync(repository, cancellationToken);
        }

        lock (gate)
        {
            if (workspaces.Values.FirstOrDefault(workspace => workspace.Info.Branch == reference && workspace.Repository == repository) is { } branched)
            {
                return commits[branched.Head];
            }

            return commits.TryGetValue(reference, out var found) ? found : Option<GitCommit>.None;
        }
    }

    internal GitCommit Record(FolderSnapshot files, string message, Option<string> parent)
    {
        lock (gate)
        {
            var text = $"{files.Digest()}\n{message}\n{parent.Match(found => found, () => string.Empty)}\n{++made}";
            var id = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..CommitLength];
            var commit = new GitCommit(id, files, message, parent);
            commits[id] = commit;

            return commit;
        }
    }

    internal async Task<Option<GitCommit>> CommitPathAsync(string repository, string path, string message, CancellationToken cancellationToken)
    {
        var head = await HeadOfAsync(repository, cancellationToken);
        var current = await FolderSnapshot.ReadEntryAsync(repository, path, cancellationToken);

        if (FolderSnapshot.Same(current, head.Files.Entry(path)))
        {
            return Option<GitCommit>.None;
        }

        var commit = Record(head.Files.With(path, current), message, head.Id);
        Move(repository, commit);

        return commit;
    }

    internal void Advance(string repository, string commit)
    {
        lock (gate)
        {
            heads[repository] = commit;
        }
    }

    internal HeldWorkspace Hold(HeldWorkspace workspace)
    {
        lock (gate)
        {
            workspaces[workspace.Info.Id] = workspace;

            return workspace;
        }
    }

    internal void Forget(WorkspaceId workspace)
    {
        lock (gate)
        {
            _ = workspaces.Remove(workspace);
        }
    }

    internal Option<HeldWorkspace> Workspace(WorkspaceId id)
    {
        lock (gate)
        {
            return workspaces.TryGetValue(id, out var found) ? found : Option<HeldWorkspace>.None;
        }
    }

    internal Option<HeldWorkspace> WorkspaceAt(string folder)
    {
        var path = Canonical(folder);

        lock (gate)
        {
            return workspaces.Values.FirstOrDefault(workspace => string.Equals(path, workspace.Info.Path, Comparison)) is { } found ? found : Option<HeldWorkspace>.None;
        }
    }

    internal IReadOnlyList<HeldWorkspace> Workspaces()
    {
        lock (gate)
        {
            return [.. workspaces.Values];
        }
    }

    internal Option<HeldWorkspace> OnBranch(string repository, string branch)
    {
        lock (gate)
        {
            return workspaces.Values.FirstOrDefault(workspace => workspace.Info.Branch == branch && workspace.Repository == repository) is { } found
                ? found
                : Option<HeldWorkspace>.None;
        }
    }

    internal Option<string> Remote(string repository, string remote)
    {
        lock (gate)
        {
            return remotes.TryGetValue((repository, remote), out var url) ? url : Option<string>.None;
        }
    }

    internal async Task<IReadOnlyList<GitCommit>> HistoryAsync(string repository, int count, CancellationToken cancellationToken)
    {
        var listed = new List<GitCommit>();

        for (var commit = await HeadOfAsync(repository, cancellationToken); commit is not null && listed.Count < count; commit = commit.Parent.Bind(Find).Match<GitCommit?>(found => found, () => null))
        {
            listed.Add(commit);
        }

        return listed;
    }

    private GitCommit? Known(string repository)
    {
        lock (gate)
        {
            if (workspaces.Values.FirstOrDefault(workspace => workspace.Info.Path == repository) is { } held)
            {
                return commits[held.Head];
            }

            return heads.TryGetValue(repository, out var head) ? commits[head] : null;
        }
    }

    private void Move(string repository, GitCommit commit)
    {
        lock (gate)
        {
            if (workspaces.Values.FirstOrDefault(workspace => workspace.Info.Path == repository) is { } held)
            {
                workspaces[held.Info.Id] = held with { Tip = commit.Id };
            }
            else
            {
                heads[repository] = commit.Id;
            }
        }
    }

    private static bool Inside(string folder, string root) =>
        folder == root || folder.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
}
