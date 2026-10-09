using System.Text.Json;
using Avala.Sdk;

namespace Avala.Runtime.Updates;

internal sealed class UpdatesFile(AvalaPaths paths)
{
    public const string FileName = "updates.json";

    public const int MaximumBytes = 16 * 1024;

    public async Task<bool> ChecksOnStartupAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(paths.Data, FileName);

        if (!File.Exists(path))
        {
            return true;
        }

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);

            if (stream.Length > MaximumBytes)
            {
                return false;
            }

            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && (!document.RootElement.TryGetProperty("checkOnStartup", out var check) || check.ValueKind == JsonValueKind.True);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }
}
