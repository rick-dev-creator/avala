using Avala.Recording.Capturing;
using Avala.Sdk;

namespace Avala.Recording.Storage;

internal sealed class EditedFiles : IEditedFiles
{
    public const int MaximumBytes = 1024 * 1024;

    public async ValueTask<Option<string>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous);

            if (stream.Length > MaximumBytes)
            {
                return Option<string>.None;
            }

            using var reader = new StreamReader(stream);

            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Option<string>.None;
        }
    }
}
