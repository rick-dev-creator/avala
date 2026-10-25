using System.Globalization;
using System.Text;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Workspaces.Testing;

internal sealed partial class InMemoryRepositories(InMemoryGit git, string scratch, TimeProvider clock) :
    IRepositories, IScratchRepositories, IRemoteRepositories, IRepositoryOrigins, IRepositoryIndex, IWorkingFiles, IWorkingCommits
{
    private const int MaximumBytes = 64 * 1024;

    private const int ShortCommit = 7;

    private const int SlugWords = 4;

    private const int SlugLength = 40;

    private const string ScratchRules = "{ \"approval\": \"merge\" }\n";

    public string Folder => InMemoryGit.Canonical(scratch);

    public ValueTask<RepositoryState> InspectAsync(string folder, CancellationToken cancellationToken)
    {
        var path = folder.Trim();

        return ValueTask.FromResult(
            path.Length == 0 || !Directory.Exists(path) ? RepositoryState.Missing
            : git.RootOf(path).IsNone ? RepositoryState.NotARepository
            : git.RootOf(path).Match(root => !git.Owns(root) && WithoutCommit(root), () => false) ? RepositoryState.NoCommit
            : RepositoryState.Ready);
    }

    public async ValueTask<Result<string, WorkspaceFailure>> InitializeAsync(string folder, CancellationToken cancellationToken)
    {
        var state = await InspectAsync(folder, cancellationToken);

        if (state is RepositoryState.Missing)
        {
            return WorkspaceFailure.FolderMissing;
        }

        if (state is RepositoryState.Ready)
        {
            return WorkspaceFailure.AlreadyARepository;
        }

        var path = InMemoryGit.Canonical(folder);
        await git.AdoptAsync(path, cancellationToken);

        return path;
    }

    public async ValueTask<Result<string, WorkspaceFailure>> CreateAsync(string parent, string name, CancellationToken cancellationToken) =>
        await CreatedAsync(Destination(parent, name), [], cancellationToken);

    public bool Holds(string repository) =>
        !string.IsNullOrWhiteSpace(repository) && InMemoryGit.Canonical(repository).StartsWith(Folder + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    public async ValueTask<Result<string, WorkspaceFailure>> CreateAsync(string instruction, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Folder);
        var day = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var name = string.Create(CultureInfo.InvariantCulture, $"{day:yyyy-MM-dd}-{Slug(instruction)}-{Guid.NewGuid().ToString("N")[..6]}");

        return await CreatedAsync(Destination(Folder, name), [(".avala/jobs.json", ScratchRules)], cancellationToken);
    }

    public ValueTask<Option<string>> RemoteAsync(string repository, string remote, CancellationToken cancellationToken) =>
        ValueTask.FromResult(git.RootOf(repository).Bind(root => git.Remote(git.WorkspaceAt(root).Match(held => held.Repository, () => root), remote)));

    public ValueTask<bool> HasCredentialHelperAsync(string repository, string url, CancellationToken cancellationToken) => ValueTask.FromResult(false);

    public ValueTask<Result<IReadOnlyList<OriginRemote>, WorkspaceFailure>> RemotesAsync(string repository, CancellationToken cancellationToken) =>
        ValueTask.FromResult(git.RootOf(repository).Match(
            root => Result<IReadOnlyList<OriginRemote>, WorkspaceFailure>.Success(
                [.. git.Remotes(git.WorkspaceAt(root).Match(held => held.Repository, () => root)).Select(remote => new OriginRemote(remote.Name, remote.Url))]),
            () => WorkspaceFailure.GitFailed));

    public async ValueTask<Result<IReadOnlyList<string>, WorkspaceFailure>> RootCommitsAsync(string repository, CancellationToken cancellationToken) =>
        await git.RootOf(repository).Match<Task<Result<IReadOnlyList<string>, WorkspaceFailure>>>(
            async root => Result<IReadOnlyList<string>, WorkspaceFailure>.Success(
                git.Owns(root) && await git.HistoryAsync(root, int.MaxValue, cancellationToken) is [.., var first] ? [first.Id] : []),
            () => Task.FromResult<Result<IReadOnlyList<string>, WorkspaceFailure>>(WorkspaceFailure.GitFailed));

    public async ValueTask<Result<string, WorkspaceFailure>> PublishAsync(string repository, string remote, string url, CancellationToken cancellationToken)
    {
        if (!IsUrl(url))
        {
            return WorkspaceFailure.InvalidRemoteUrl;
        }

        var root = git.RootOf(repository).Match(found => found, () => string.Empty);

        if (root.Length == 0)
        {
            return WorkspaceFailure.NoBaseBranch;
        }

        if (git.Remote(root, remote).Match(existing => existing != url, () => false))
        {
            return WorkspaceFailure.RemoteExists;
        }

        if (!Reachable(url))
        {
            return WorkspaceFailure.RemoteNotFound;
        }

        git.AddRemote(root, remote, url);

        if (Path.IsPathFullyQualified(url))
        {
            await git.PublishAsync(root, url, cancellationToken);
        }

        return InMemoryGit.MainBranch;
    }

    public async ValueTask<Result<string, WorkspaceFailure>> CloneAsync(string url, string parent, string name, CancellationToken cancellationToken)
    {
        var source = url.Trim();

        if (!IsUrl(source))
        {
            return WorkspaceFailure.InvalidRemoteUrl;
        }

        if (!Reachable(source))
        {
            return WorkspaceFailure.RemoteNotFound;
        }

        if (!Destination(parent, name).TryGetValue(out var path, out var failure))
        {
            return failure;
        }

        if (Path.IsPathFullyQualified(source))
        {
            await git.CloneAsync(source, path, cancellationToken);
        }
        else
        {
            await git.CreateAsync(path, [], cancellationToken);
        }

        git.AddRemote(path, "origin", source);

        return InMemoryGit.Canonical(path);
    }

    public async ValueTask<IReadOnlyList<string>> FilesAsync(string folder, int limit, CancellationToken cancellationToken)
    {
        var root = git.RootOf(folder).Match(found => found, () => string.Empty);

        if (root.Length == 0)
        {
            return [];
        }

        var tracked = (await git.HeadOfAsync(root, cancellationToken)).Files;
        var present = await FolderSnapshot.ReadAsync(root, tracked, cancellationToken);
        var under = FolderSnapshot.Normal(Path.GetRelativePath(root, InMemoryGit.Canonical(folder)));
        var prefix = under == "." ? string.Empty : under + "/";

        return
        [
            .. present.Files.Keys.Where(path => !tracked.Files.ContainsKey(path)).Concat(tracked.Files.Keys)
                .Where(path => path.StartsWith(prefix, StringComparison.Ordinal))
                .Select(path => path[prefix.Length..])
                .Take(limit),
        ];
    }

    public async ValueTask<IReadOnlyList<CommitSummary>> CommitsAsync(string folder, int count, CancellationToken cancellationToken) =>
        await git.RootOf(folder).Match<Task<IReadOnlyList<CommitSummary>>>(
            async root => [.. (await git.HistoryAsync(root, count, cancellationToken)).Select(commit => new CommitSummary(commit.Id[..ShortCommit], commit.Message.Split('\n')[0]))],
            () => Task.FromResult<IReadOnlyList<CommitSummary>>([]));

    public async ValueTask<Result<Option<string>, WorkspaceFailure>> ReadAsync(string repository, string path, CancellationToken cancellationToken)
    {
        if (!Contained(repository, path).TryGetValue(out var file, out var failure))
        {
            return failure;
        }

        if (!File.Exists(file))
        {
            return Option<string>.None;
        }

        return new FileInfo(file).Length > MaximumBytes ? WorkspaceFailure.FileTooLarge : Option<string>.Some(await File.ReadAllTextAsync(file, cancellationToken));
    }

    public async ValueTask<Result<string, WorkspaceFailure>> WriteAsync(string repository, string path, string content, CancellationToken cancellationToken)
    {
        if (content.Length > MaximumBytes)
        {
            return WorkspaceFailure.FileTooLarge;
        }

        if (!Contained(repository, path).TryGetValue(out var file, out var failure))
        {
            return failure;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, content, cancellationToken);

        return file;
    }

    public ValueTask<Result<Option<string>, WorkspaceFailure>> LocateAsync(string repository, string path, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Contained(repository, path).Map(file => File.Exists(file) ? Option<string>.Some(file) : Option<string>.None));

    public async ValueTask<Result<Option<byte[]>, WorkspaceFailure>> ReadBytesAsync(string repository, string path, int limit, CancellationToken cancellationToken)
    {
        if (!Contained(repository, path).TryGetValue(out var file, out var failure))
        {
            return failure;
        }

        if (!File.Exists(file))
        {
            return Option<byte[]>.None;
        }

        return new FileInfo(file).Length > limit ? WorkspaceFailure.FileTooLarge : Option<byte[]>.Some(await File.ReadAllBytesAsync(file, cancellationToken));
    }

    public async ValueTask<Result<WorkingCommit, WorkspaceFailure>> CommitAsync(string repository, string path, string message, CancellationToken cancellationToken)
    {
        var root = git.RootOf(repository).Match(found => found, () => string.Empty);

        if (root.Length == 0)
        {
            return WorkspaceFailure.NotAGitRepository;
        }

        if (!Contained(root, path).TryGetValue(out _, out var failure))
        {
            return failure;
        }

        var branch = git.WorkspaceAt(root).Match(held => held.Info.Branch, () => InMemoryGit.MainBranch);

        return (await git.CommitPathAsync(root, path, message, cancellationToken))
            .Map(commit => new WorkingCommit(commit.Id, branch))
            .ToResult(WorkspaceFailure.FileUnchanged);
    }

    private Result<string, WorkspaceFailure> Contained(string repository, string path)
    {
        var root = git.RootOf(repository).Match(found => found, () => string.Empty);

        if (root.Length == 0)
        {
            return WorkspaceFailure.NotAGitRepository;
        }

        var full = Path.GetFullPath(Path.Combine(root, path));
        var inside = Path.GetRelativePath(root, full);

        return Path.IsPathRooted(path) || inside.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(inside)
            ? WorkspaceFailure.OutsideRepository
            : full;
    }

    private async Task<Result<string, WorkspaceFailure>> CreatedAsync(Result<string, WorkspaceFailure> destination, IReadOnlyList<(string Path, string Content)> seed, CancellationToken cancellationToken)
    {
        if (!destination.TryGetValue(out var path, out var failure))
        {
            return failure;
        }

        await git.CreateAsync(path, seed, cancellationToken);

        return InMemoryGit.Canonical(path);
    }

    private static Result<string, WorkspaceFailure> Destination(string parent, string name)
    {
        var named = name.Trim();

        if (named.Length == 0 || named is "." or ".." || named.IndexOfAny(['/', '\\']) >= 0 || named.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return WorkspaceFailure.InvalidRepositoryName;
        }

        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent.Trim()))
        {
            return WorkspaceFailure.FolderMissing;
        }

        var path = Path.Combine(Path.GetFullPath(parent.Trim()), named);

        return Directory.Exists(path) || File.Exists(path) ? WorkspaceFailure.FolderExists : path;
    }

    private bool Reachable(string url) =>
        Path.IsPathFullyQualified(url) ? git.IsRepository(url) : url.Contains("://", StringComparison.Ordinal) || RemoteAddress().IsMatch(url);

    [System.Text.RegularExpressions.GeneratedRegex(@"^[\w.-]+@[\w.-]+:|^[\w.-]{2,}:[^/\\]")]
    private static partial System.Text.RegularExpressions.Regex RemoteAddress();

    private static bool WithoutCommit(string root)
    {
        var refs = Path.Combine(root, ".git", "refs", "heads");

        return Directory.Exists(Path.Combine(root, ".git"))
            && !File.Exists(Path.Combine(root, ".git", "packed-refs"))
            && (!Directory.Exists(refs) || !Directory.EnumerateFiles(refs, "*", SearchOption.AllDirectories).Any());
    }

    private static bool IsUrl(string url) => url.Length > 0 && !url.StartsWith('-') && !url.Any(char.IsControl);

    private static string Slug(string instruction)
    {
        var plain = new string([.. instruction.ToLowerInvariant().Select(letter => char.IsAsciiLetterOrDigit(letter) ? letter : ' ')]);
        var slug = string.Join('-', plain.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(SlugWords));
        var cut = slug.Length > SlugLength ? slug[..SlugLength].TrimEnd('-') : slug;

        return cut.Length == 0 ? "job" : cut;
    }
}
