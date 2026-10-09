using Avala.Recording.Capturing;
using Avala.Recording.Recordings;
using Avala.Sdk;
using Microsoft.Extensions.Logging;

namespace Avala.Recording.Settings;

internal sealed partial class SettingsFile(AvalaPaths paths, ILogger<SettingsFile> logger) : IRecordingSettings
{
    public const string FileName = "recording.json";

    public const int MaximumBytes = 16 * 1024;

    private Task<RecordingSettings>? loading;

    public async ValueTask<RecordingSettings> LoadAsync(CancellationToken cancellationToken) =>
        await LazyInitializer.EnsureInitialized(ref loading, () => ReadAsync(Path.Combine(paths.Data, FileName))).WaitAsync(cancellationToken);

    private async Task<RecordingSettings> ReadAsync(string path) =>
        !File.Exists(path)
            ? RecordingSettings.Off
            : (await ReadTextAsync(path)).Bind(SettingsParser.Parse).Match(
                settings => settings,
                error =>
                {
                    LogRejected(path, error);
                    return RecordingSettings.Off;
                });

    private static async Task<Result<string, RecordingError>> ReadTextAsync(string path)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);

            if (stream.Length > MaximumBytes)
            {
                return RecordingError.TooLarge;
            }

            using var reader = new StreamReader(stream);

            return await reader.ReadToEndAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return RecordingError.Unreadable;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sessions are not recorded: {Path} was rejected as {Error}")]
    private partial void LogRejected(string path, RecordingError error);
}
