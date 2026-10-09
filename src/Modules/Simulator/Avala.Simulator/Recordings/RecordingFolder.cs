using Avala.Sdk;
using Avala.Simulator.Playback;
using Avala.Simulator.Scenarios;

namespace Avala.Simulator.Recordings;

internal sealed class RecordingFolder(AvalaPaths paths) : IRecordedScenarios
{
    public const string FolderName = "recordings";

    public const int MaximumBytes = 16 * 1024 * 1024;

    public async Task<Result<RecordedSession, ReplayError>> LoadAsync(string recording, string workingDirectory, CancellationToken cancellationToken)
    {
        var path = Path.Combine(paths.Folder(FolderName), $"{recording}.json");

        return !File.Exists(path)
            ? ReplayError.NotFound
            : (await ReadAsync(path, cancellationToken)).Bind(text => RecordingParser.Parse(text, workingDirectory));
    }

    private static async Task<Result<string, ReplayError>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);

            if (stream.Length > MaximumBytes)
            {
                return ReplayError.Unreadable;
            }

            using var reader = new StreamReader(stream);

            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ReplayError.Unreadable;
        }
    }
}
