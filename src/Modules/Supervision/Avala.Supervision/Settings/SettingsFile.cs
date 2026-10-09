using Avala.Sdk;
using Avala.Supervision.Contracts;
using Avala.Supervision.Supervising;
using Avala.Supervision.Watching;

namespace Avala.Supervision.Settings;

internal sealed class SettingsFile(AvalaPaths paths) : ISupervisionSettings, IAsyncDisposable
{
    public const string FileName = "supervision.json";

    public const int MaximumBytes = 16 * 1024;

    private readonly SerialExecutor writes = new();
    private Task<SupervisionSettings>? loading;

    private string FilePath => Path.Combine(paths.Data, FileName);

    public async ValueTask<SupervisionSettings> LoadAsync(CancellationToken cancellationToken) =>
        await LazyInitializer.EnsureInitialized(ref loading, () => ReadAsync(FilePath)).WaitAsync(cancellationToken);

    public async ValueTask<Result<SupervisionSettings, SupervisionError>> ChangeSilenceAsync(TimeSpan silence, CancellationToken cancellationToken) =>
        await SettingsParser.Window(silence.TotalSeconds).Match(
            window => writes.RunAsync(token => WriteAsync(window, token), cancellationToken),
            error => Task.FromResult(Result<SupervisionSettings, SupervisionError>.Failure(error)));

    public ValueTask DisposeAsync() => writes.DisposeAsync();

    private async Task<Result<SupervisionSettings, SupervisionError>> WriteAsync(TimeSpan silence, CancellationToken cancellationToken)
    {
        var temporary = $"{FilePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(paths.Data);
            await File.WriteAllTextAsync(temporary, SettingsParser.Text(silence), cancellationToken);
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return SupervisionError.Unwritable;
        }

        var settings = new SupervisionSettings(silence, SettingsFileStatus.Applied, Option<SupervisionError>.None);
        Volatile.Write(ref loading, Task.FromResult(settings));

        return settings;
    }

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
