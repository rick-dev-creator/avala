using System.Text.Json;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Permissions.Answering;

internal interface IReadableFolders
{
    ValueTask<IReadOnlyList<string>> FoldersAsync(CancellationToken cancellationToken);
}

internal sealed class ReadableFolders(AvalaPaths paths, IProjectsFolder projects) : IReadableFolders
{
    public const string FileName = "readable.json";

    public const int MaximumBytes = 16 * 1024;

    private Kept? kept;

    private string FilePath => Path.Combine(paths.Data, FileName);

    public async ValueTask<IReadOnlyList<string>> FoldersAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(FilePath))
        {
            return [(await projects.ReadAsync(cancellationToken)).Folder];
        }

        var written = File.GetLastWriteTimeUtc(FilePath);

        if (Volatile.Read(ref kept) is { } known && known.Written == written)
        {
            return known.Folders;
        }

        var folders = Parse(await ReadAsync(FilePath, cancellationToken));
        Volatile.Write(ref kept, new Kept(written, folders));

        return folders;
    }

    public static IReadOnlyList<string> Parse(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;

            return root.ValueKind == JsonValueKind.Object
                && root.EnumerateObject().All(property => property.NameEquals("folders"))
                && root.TryGetProperty("folders", out var folders)
                && folders.ValueKind == JsonValueKind.Array
                && folders.EnumerateArray().All(folder => folder.ValueKind == JsonValueKind.String)
                    ? [.. folders.EnumerateArray().Select(folder => Expanded(folder.GetString() ?? string.Empty)).Where(Path.IsPathFullyQualified)]
                    : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string Expanded(string folder) =>
        folder == "~" || folder.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), folder[1..])
            : folder;

    private static async Task<string> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);

            if (stream.Length > MaximumBytes)
            {
                return string.Empty;
            }

            using var reader = new StreamReader(stream);

            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private sealed record Kept(DateTime Written, IReadOnlyList<string> Folders);
}
