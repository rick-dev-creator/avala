using System.Text.Json;
using System.Text.Json.Nodes;
using Avala.ClaudeCode.Conversations;
using Avala.Sdk;

namespace Avala.ClaudeCode.Preferences;

internal sealed class PhoneStartFile(AvalaPaths paths) : IPhoneStart, IStartupTask
{
    public const string FileName = "claude-code.json";

    public const string Key = "startJobsOnPhone";

    private bool startsOn;

    public string Path => System.IO.Path.Combine(paths.Data, FileName);

    public bool StartsOn => Volatile.Read(ref startsOn);

    public StartupStage Stage => StartupStage.Open;

    public async Task RunAsync(CancellationToken cancellationToken) => Volatile.Write(ref startsOn, await ReadAsync(cancellationToken));

    public async Task<bool> ChooseAsync(bool on, CancellationToken cancellationToken)
    {
        var temporary = $"{Path}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(paths.Data);
            await File.WriteAllTextAsync(temporary, new JsonObject { [Key] = on }.ToJsonString(), cancellationToken);
            File.Move(temporary, Path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        Volatile.Write(ref startsOn, on);

        return true;
    }

    private async Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(Path))
            {
                return false;
            }

            await using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            var read = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken);

            return read?[Key]?.GetValueKind() == JsonValueKind.True;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }
}
