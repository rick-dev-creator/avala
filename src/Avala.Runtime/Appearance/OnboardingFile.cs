using System.Text.Json;
using Avala.Sdk;
using Avala.Sdk.Appearance;

namespace Avala.Runtime.Appearance;

internal sealed class OnboardingFile(AvalaPaths paths) : IOnboarding
{
    public const string FileName = "onboarding.json";

    private string FilePath => Path.Combine(paths.Data, FileName);

    public async ValueTask<bool> IsDoneAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(FilePath) || new FileInfo(FilePath).Length > AppearanceFile.MaximumBytes)
            {
                return false;
            }

            await using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("onboarded", out var onboarded)
                && onboarded.ValueKind == JsonValueKind.True;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    public async ValueTask<bool> FinishAsync(CancellationToken cancellationToken)
    {
        var temporary = $"{FilePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(paths.Data);
            await File.WriteAllTextAsync(temporary, "{\n  \"onboarded\": true\n}\n", cancellationToken);
            File.Move(temporary, FilePath, overwrite: true);

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
