using System.Text.Json;
using Avala.Fleet.Connecting;
using Avala.Sdk;

namespace Avala.Fleet.FleetFiles;

internal sealed class FleetFileStore(AvalaPaths paths) : IFleetFile, IAsyncDisposable
{
    public const string FileName = "fleet.json";

    public const int MaximumBytes = 16 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, RespectNullableAnnotations = true };

    private readonly SerialExecutor writes = new();

    public string Path => System.IO.Path.Combine(paths.Data, FileName);

    public async Task<Option<FleetSettings>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(Path))
        {
            return Option<FleetSettings>.None;
        }

        try
        {
            await using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);

            if (stream.Length > MaximumBytes)
            {
                return Option<FleetSettings>.None;
            }

            return await JsonSerializer.DeserializeAsync<FleetFile>(stream, Json, cancellationToken) is { Server.Length: > 0 } read
                ? new FleetSettings(read.Server, read.Instance, read.InstanceName, read.Enabled, read.KeyRefused)
                : Option<FleetSettings>.None;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Option<FleetSettings>.None;
        }
    }

    public Task<bool> WriteAsync(FleetSettings settings, CancellationToken cancellationToken) =>
        writes.RunAsync(
            async token =>
            {
                var temporary = $"{Path}.writing";

                try
                {
                    Directory.CreateDirectory(paths.Data);
                    await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new FleetFile(settings.Server, settings.Instance, settings.InstanceName, settings.Enabled, settings.KeyRefused), Json), token);
                    File.Move(temporary, Path, overwrite: true);

                    return true;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return false;
                }
            },
            cancellationToken);

    public ValueTask DisposeAsync() => writes.DisposeAsync();

    public Task<bool> DeleteAsync(CancellationToken cancellationToken) =>
        writes.RunAsync(
            _ =>
            {
                try
                {
                    File.Delete(Path);

                    return Task.FromResult(true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return Task.FromResult(false);
                }
            },
            cancellationToken);
}

internal sealed record FleetFile(string Server, Guid Instance, string InstanceName, bool Enabled, bool KeyRefused);
