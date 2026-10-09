using Avala.Resources.Contracts;
using Avala.Resources.Tracking;
using Avala.Sdk;

namespace Avala.Resources.Settings;

internal sealed class ResourceSettingsFile(AvalaPaths paths) : IResourceSettings
{
    public const string FileName = "resources.json";

    public const int MaximumBytes = 16 * 1024;

    private Task<ResourceSettings>? loading;

    public async ValueTask<ResourceSettings> LoadAsync(CancellationToken cancellationToken) =>
        await LazyInitializer.EnsureInitialized(ref loading, () => ReadAsync(Path.Combine(paths.Data, FileName))).WaitAsync(cancellationToken);

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
