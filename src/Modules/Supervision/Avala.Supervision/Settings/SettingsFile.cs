using Avala.Sdk;
using Avala.Supervision.Contracts;
using Avala.Supervision.Supervising;
using Avala.Supervision.Watching;

namespace Avala.Supervision.Settings;

internal sealed class SettingsFile(AvalaPaths paths) : ISupervisionSettings
{
    public const string FileName = "supervision.json";

    public const int MaximumBytes = 16 * 1024;

    private Task<SupervisionSettings>? loading;

    public async ValueTask<SupervisionSettings> LoadAsync(CancellationToken cancellationToken) =>
        await LazyInitializer.EnsureInitialized(ref loading, () => ReadAsync(Path.Combine(paths.Data, FileName))).WaitAsync(cancellationToken);

    private static async Task<SupervisionSettings> ReadAsync(string path) =>
        !File.Exists(path)
            ? new SupervisionSettings(JobWatch.DefaultSilence, SettingsFileStatus.Absent, Option<SupervisionError>.None)
            : (await ReadTextAsync(path)).Bind(SettingsParser.Parse).Match(
                silence => new SupervisionSettings(silence, SettingsFileStatus.Applied, Option<SupervisionError>.None),
                error => new SupervisionSettings(JobWatch.DefaultSilence, SettingsFileStatus.Rejected, error));

    private static async Task<Result<string, SupervisionError>> ReadTextAsync(string path)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);

            if (stream.Length > MaximumBytes)
            {
                return SupervisionError.TooLarge;
            }

            using var reader = new StreamReader(stream);

            return await reader.ReadToEndAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return SupervisionError.Unreadable;
        }
    }
}
