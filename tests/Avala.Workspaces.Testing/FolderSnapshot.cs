using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Avala.Sdk;

namespace Avala.Workspaces.Testing;

internal sealed record FolderSnapshot(ImmutableSortedDictionary<string, byte[]> Files)
{
    private const string GitEntry = ".git";

    public static FolderSnapshot Empty { get; } = new(ImmutableSortedDictionary.Create<string, byte[]>(StringComparer.Ordinal));

    public static FolderSnapshot Of(params (string Path, string Content)[] files) =>
        new(files.Aggregate(Empty.Files, (all, file) => all.SetItem(Normal(file.Path), Encoding.UTF8.GetBytes(file.Content))));

    public static async Task<FolderSnapshot> ReadAsync(string folder, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folder))
        {
            return Empty;
        }

        var files = Empty.Files.ToBuilder();

        foreach (var file in Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            var relative = Normal(Path.GetRelativePath(folder, file));

            if (relative != GitEntry && !relative.StartsWith(GitEntry + "/", StringComparison.Ordinal))
            {
                files[relative] = await File.ReadAllBytesAsync(file, cancellationToken);
            }
        }

        return new FolderSnapshot(files.ToImmutable());
    }

    public async Task WriteToAsync(string folder, CancellationToken cancellationToken)
    {
        foreach (var stale in (await ReadAsync(folder, cancellationToken)).Files.Keys.Where(path => !Files.ContainsKey(path)))
        {
            File.Delete(Path.Combine(folder, stale));
        }

        foreach (var (path, content) in Files)
        {
            var file = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllBytesAsync(file, content, cancellationToken);
        }
    }

    public static async Task<Option<byte[]>> ReadEntryAsync(string folder, string path, CancellationToken cancellationToken)
    {
        var file = Path.Combine(folder, path);

        return File.Exists(file) ? await File.ReadAllBytesAsync(file, cancellationToken) : Option<byte[]>.None;
    }

    public Option<byte[]> Entry(string path) => Files.TryGetValue(Normal(path), out var content) ? content : Option<byte[]>.None;

    public FolderSnapshot With(string path, Option<byte[]> content) =>
        new(content.Match(found => Files.SetItem(Normal(path), found), () => Files.Remove(Normal(path))));

    public bool SameAs(FolderSnapshot other) =>
        Files.Count == other.Files.Count && Files.All(pair => other.Files.TryGetValue(pair.Key, out var content) && content.AsSpan().SequenceEqual(pair.Value));

    public string Digest()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var (path, content) in Files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(path));
            hash.AppendData([0]);
            hash.AppendData(content);
            hash.AppendData([0]);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public static bool Same(Option<byte[]> left, Option<byte[]> right) =>
        left.Match(found => right.Match(other => found.AsSpan().SequenceEqual(other), () => false), () => right.IsNone);

    public static string Normal(string path) => path.Replace('\\', '/');
}
