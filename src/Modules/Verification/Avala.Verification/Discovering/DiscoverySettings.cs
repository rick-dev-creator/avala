using System.Text.Json;
using Avala.Sdk;

namespace Avala.Verification.Discovering;

internal interface IDiscoverySettings
{
    ValueTask<bool> DiscoversAsync(CancellationToken cancellationToken);
}

internal sealed class DiscoverySettings(AvalaPaths paths) : IDiscoverySettings
{
    public const string FileName = "verification.json";

    private const int MaximumBytes = 16 * 1024;

    public async ValueTask<bool> DiscoversAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(paths.Data, FileName);

        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaximumBytes)
            {
                return true;
            }

            return Discovers(await File.ReadAllTextAsync(path, cancellationToken));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    public static bool Discovers(string settings)
    {
        try
        {
            using var document = JsonDocument.Parse(settings);

            return document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("discoverChecks", out var discover)
                || discover.ValueKind != JsonValueKind.False;
        }
        catch (JsonException)
        {
            return true;
        }
    }
}
