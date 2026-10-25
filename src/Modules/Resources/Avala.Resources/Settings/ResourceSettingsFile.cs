using System.Text.Json;
using System.Text.Json.Nodes;
using Avala.Resources.Contracts;
using Avala.Resources.Tracking;
using Avala.Sdk;

namespace Avala.Resources.Settings;

internal sealed class ResourceSettingsFile(AvalaPaths paths) : IResourceSettings, IAsyncDisposable
{
    public const string FileName = "resources.json";

    public const int MaximumBytes = 16 * 1024;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly SerialExecutor writes = new();
    private Task<ResourceSettings>? loading;

    private string FilePath => Path.Combine(paths.Data, FileName);

    public async ValueTask<ResourceSettings> LoadAsync(CancellationToken cancellationToken) =>
        await LazyInitializer.EnsureInitialized(ref loading, () => ReadAsync(FilePath)).WaitAsync(cancellationToken);

    public async ValueTask<Result<ResourceSettings, ResourceError>> ChangeWorktreesAsync(WorktreePolicy policy, CancellationToken cancellationToken) =>
        await writes.RunAsync(token => WriteAsync(policy, token), cancellationToken);

    public ValueTask DisposeAsync() => writes.DisposeAsync();

    private async Task<Result<ResourceSettings, ResourceError>> WriteAsync(WorktreePolicy policy, CancellationToken cancellationToken)
    {
        var current = await LoadAsync(cancellationToken);

        if (current.File == ResourceFileStatus.Rejected)
        {
            return current.Error.Match(error => error, () => ResourceError.Malformed);
        }

        if (!(await RootAsync(current.File)).TryGetValue(out var root, out var failure))
        {
            return failure;
        }

        root[ResourceSettingsParser.Worktrees] = ResourceSettingsParser.Node(policy, current.Reconcile);
        var text = $"{root.ToJsonString(Indented)}\n";

        if (!ResourceSettingsParser.Parse(text).TryGetValue(out var changed, out failure))
        {
            return failure;
        }

        var temporary = $"{FilePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(paths.Data);
            await File.WriteAllTextAsync(temporary, text, cancellationToken);
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResourceError.Unwritable;
        }

        var applied = changed with { File = ResourceFileStatus.Applied };
        Volatile.Write(ref loading, Task.FromResult(applied));

        return applied;
    }

    private async Task<Result<JsonObject, ResourceError>> RootAsync(ResourceFileStatus status) =>
        status == ResourceFileStatus.Absent
            ? new JsonObject()
            : (await ReadTextAsync(FilePath)).Bind<JsonObject>(text => JsonNode.Parse(text) is JsonObject root ? root : ResourceError.Malformed);

    private static async Task<ResourceSettings> ReadAsync(string path) =>
        !File.Exists(path)
            ? ResourceSettingsParser.Defaults with { File = ResourceFileStatus.Absent }
            : (await ReadTextAsync(path)).Bind(ResourceSettingsParser.Parse).Match(
                settings => settings with { File = ResourceFileStatus.Applied },
                error => ResourceSettingsParser.Defaults with { File = ResourceFileStatus.Rejected, Error = error });

    private static async Task<Result<string, ResourceError>> ReadTextAsync(string path)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);

            if (stream.Length > MaximumBytes)
            {
                return ResourceError.TooLarge;
            }

            using var reader = new StreamReader(stream);

            return await reader.ReadToEndAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResourceError.Unreadable;
        }
    }
}
