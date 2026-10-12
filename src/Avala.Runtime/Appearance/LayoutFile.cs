using System.Text.Json;
using Avala.Sdk;
using Avala.Sdk.Appearance;

namespace Avala.Runtime.Appearance;

internal sealed class LayoutFile(AvalaPaths paths) : IWindowLayout, IAsyncDisposable
{
    public const string FileName = "layout.json";

    private readonly SerialExecutor writes = new();

    private string FilePath => Path.Combine(paths.Data, FileName);

    public async ValueTask<WindowLayout> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(FilePath) || new FileInfo(FilePath).Length > AppearanceFile.MaximumBytes)
            {
                return WindowLayout.Default;
            }

            await using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("sidebar", out var sidebar)
                && sidebar.ValueKind == JsonValueKind.String
                && sidebar.GetString() == "collapsed"
                    ? new WindowLayout(true)
                    : WindowLayout.Default;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return WindowLayout.Default;
        }
    }

    public async ValueTask RememberAsync(WindowLayout layout, CancellationToken cancellationToken) =>
        await writes.RunAsync(token => WriteAsync(layout, token), cancellationToken);

    public ValueTask DisposeAsync() => writes.DisposeAsync();

    private async Task<bool> WriteAsync(WindowLayout layout, CancellationToken cancellationToken)
    {
        var temporary = $"{FilePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(paths.Data);
            await File.WriteAllTextAsync(temporary, $"{{\n  \"sidebar\": \"{(layout.IsSidebarCollapsed ? "collapsed" : "shown")}\"\n}}\n", cancellationToken);
            File.Move(temporary, FilePath, overwrite: true);

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
