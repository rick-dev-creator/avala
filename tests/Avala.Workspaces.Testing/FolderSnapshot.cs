using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Avala.Sdk;

namespace Avala.Workspaces.Testing;

internal sealed record FolderSnapshot(ImmutableSortedDictionary<string, byte[]> Files, ImmutableDictionary<string, string> Modes)
{
    public const string Regular = "100644";

    public const string Executable = "100755";

    public const string Link = "120000";

    private const string GitEntry = ".git";

    public static FolderSnapshot Empty { get; } = new(ImmutableSortedDictionary.Create<string, byte[]>(GitPathOrder.Instance), ImmutableDictionary.Create<string, string>(StringComparer.Ordinal));

    public static FolderSnapshot Of(params (string Path, string Content)[] files) =>
        files.Aggregate(Empty, (all, file) => all.With(file.Path, Encoding.UTF8.GetBytes(file.Content), Regular));

    public static Task<FolderSnapshot> ReadAsync(string folder, CancellationToken cancellationToken) => ReadAsync(folder, Empty, cancellationToken);

    public static async Task<FolderSnapshot> ReadAsync(string folder, FolderSnapshot tracked, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folder))
        {
            return Empty;
        }

        var ignore = await GitIgnore.ReadAsync(folder, cancellationToken);
        var snapshot = Empty;

        foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
        {
            var relative = Normal(Path.GetRelativePath(folder, entry.FullName));

            if (relative.Split('/').Contains(GitEntry) || Inside(relative, folder) || (!tracked.Files.ContainsKey(relative) && ignore.Ignores(relative)))
            {
                continue;
            }

            snapshot = entry switch
            {
                { LinkTarget: { } target } => snapshot.With(relative, Encoding.UTF8.GetBytes(target), Link),
                FileInfo file => snapshot.With(relative, await File.ReadAllBytesAsync(file.FullName, cancellationToken), ModeOf(file)),
                _ => snapshot,
            };
        }

        return snapshot;
    }

    public static async Task<Option<byte[]>> ReadEntryAsync(string folder, string path, CancellationToken cancellationToken)
    {
        var file = new FileInfo(Path.Combine(folder, path));

        return file.LinkTarget is { } target ? Encoding.UTF8.GetBytes(target)
            : file.Exists ? await File.ReadAllBytesAsync(file.FullName, cancellationToken)
            : Option<byte[]>.None;
    }

    public Task WriteToAsync(string folder, CancellationToken cancellationToken) => Empty.ApplyAsync(this, folder, cancellationToken);

    public async Task ApplyAsync(FolderSnapshot target, string folder, CancellationToken cancellationToken)
    {
        foreach (var gone in Files.Keys.Where(path => !target.Files.ContainsKey(path)))
        {
            Remove(Path.Combine(folder, gone));
            Prune(Path.GetDirectoryName(Path.Combine(folder, gone)), folder);
        }

        foreach (var (path, content) in target.Files.Where(file => !SameEntry(this, target, file.Key)))
        {
            var file = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            Remove(file);

            if (target.ModeOf(path) == Link)
            {
                _ = File.CreateSymbolicLink(file, Encoding.UTF8.GetString(content));
            }
            else
            {
                await File.WriteAllBytesAsync(file, content, cancellationToken);
                MarkExecutable(file, target.ModeOf(path) == Executable);
            }
        }
    }

    public async Task<bool> TrackedUnchangedAsync(string folder, CancellationToken cancellationToken)
    {
        var current = await ReadAsync(folder, this, cancellationToken);

        return Files.Keys.All(path => SameEntry(this, current, path));
    }

    public Option<byte[]> Entry(string path) => Files.TryGetValue(Normal(path), out var content) ? content : Option<byte[]>.None;

    public string ModeOf(string path) => Modes.TryGetValue(Normal(path), out var mode) ? mode : Regular;

    public FolderSnapshot With(string path, Option<byte[]> content) =>
        content.Match(found => With(path, found, ModeOf(path)), () => new FolderSnapshot(Files.Remove(Normal(path)), Modes.Remove(Normal(path))));

    public FolderSnapshot With(string path, byte[] content, string mode) =>
        new(Files.SetItem(Normal(path), content), mode == Regular ? Modes.Remove(Normal(path)) : Modes.SetItem(Normal(path), mode));

    public FolderSnapshot WithEntryOf(FolderSnapshot other, string path) =>
        other.Entry(path).Match(found => With(path, found, other.ModeOf(path)), () => With(path, Option<byte[]>.None));

    public bool SameAs(FolderSnapshot other) =>
        Files.Count == other.Files.Count && Files.Keys.All(path => SameEntry(this, other, path));

    public static bool SameEntry(FolderSnapshot left, FolderSnapshot right, string path) =>
        Same(left.Entry(path), right.Entry(path)) && left.ModeOf(path) == right.ModeOf(path);

    public string Digest()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var (path, content) in Files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes($"{path}\0{ModeOf(path)}\0"));
            hash.AppendData(content);
            hash.AppendData([0]);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public static bool Same(Option<byte[]> left, Option<byte[]> right) =>
        left.Match(found => right.Match(other => found.AsSpan().SequenceEqual(other), () => false), () => right.IsNone);

    public static string Normal(string path) => path.Replace('\\', '/');

    private static bool Inside(string relative, string folder)
    {
        for (var current = Path.GetDirectoryName(Path.Combine(folder, relative)); current is not null && current.Length > folder.Length; current = Path.GetDirectoryName(current))
        {
            if (new DirectoryInfo(current).LinkTarget is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static string ModeOf(FileInfo file) =>
        !OperatingSystem.IsWindows() && file.UnixFileMode.HasFlag(UnixFileMode.UserExecute) ? Executable : Regular;

    private static void MarkExecutable(string file, bool executable)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        const UnixFileMode Execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        var mode = File.GetUnixFileMode(file);
        File.SetUnixFileMode(file, executable ? mode | Execute : mode & ~Execute);
    }

    private static void Prune(string? directory, string root)
    {
        var top = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

        for (var current = directory; current is not null && Path.TrimEndingDirectorySeparator(Path.GetFullPath(current)).Length > top.Length; current = Path.GetDirectoryName(current))
        {
            if (!Directory.Exists(current) || Directory.EnumerateFileSystemEntries(current).Any())
            {
                return;
            }

            Directory.Delete(current);
        }
    }

    private static void Remove(string file)
    {
        var entry = new FileInfo(file);

        if (entry.Exists || entry.LinkTarget is not null)
        {
            entry.Delete();
        }
        else if (Directory.Exists(file))
        {
            Directory.Delete(file, recursive: true);
        }
    }
}

internal sealed class GitPathOrder : IComparer<string>
{
    public static GitPathOrder Instance { get; } = new();

    public int Compare(string? x, string? y) =>
        Encoding.UTF8.GetBytes(x ?? string.Empty).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(y ?? string.Empty));
}
