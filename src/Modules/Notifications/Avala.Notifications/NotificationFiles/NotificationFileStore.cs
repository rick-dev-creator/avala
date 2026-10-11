using Avala.Notifications.Contracts;
using Avala.Notifications.Records;
using Avala.Sdk;

namespace Avala.Notifications.NotificationFiles;

internal sealed class NotificationFileStore(AvalaPaths paths) : INotificationFile
{
    public const string FileName = "notifications.json";

    public string Path => System.IO.Path.Combine(paths.Data, FileName);

    public async ValueTask<NotificationFile> ReadAsync(CancellationToken cancellationToken) =>
        !File.Exists(Path)
            ? new NotificationFile(Path, NotificationFileStatus.Absent)
            : (await TextAsync(cancellationToken)).Bind(NotificationFileParser.Parse).Match(
                settings => new NotificationFile(Path, NotificationFileStatus.Applied) { Settings = settings },
                error => new NotificationFile(Path, NotificationFileStatus.Rejected) { Error = error });

    public async ValueTask<Result<NotificationFile, NotificationError>> WriteAsync(NotificationSettings settings, CancellationToken cancellationToken)
    {
        var temporary = $"{Path}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(paths.Data);
            await File.WriteAllTextAsync(temporary, NotificationText.Write(settings), cancellationToken);
            File.Move(temporary, Path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return NotificationError.Unwritable;
        }

        return await ReadAsync(cancellationToken);
    }

    private async Task<Result<string, NotificationError>> TextAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);

            if (stream.Length > NotificationFileParser.MaximumBytes)
            {
                return NotificationError.TooLarge;
            }

            using var reader = new StreamReader(stream);

            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return NotificationError.Unreadable;
        }
    }
}
