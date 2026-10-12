using System.Globalization;
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
                ? new WindowLayout(IsCollapsed(document.RootElement), Width(document.RootElement)) { PreviewShare = Share(document.RootElement) }
                : WindowLayout.Default;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return WindowLayout.Default;
        }
    }

    public async ValueTask RememberAsync(Func<WindowLayout, WindowLayout> change, CancellationToken cancellationToken) =>
        await writes.RunAsync(async token => await WriteAsync(change(await ReadAsync(token)), token), cancellationToken);

    public ValueTask DisposeAsync() => writes.DisposeAsync();

    private static string Json(WindowLayout layout) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{{\n  \"sidebar\": \"{(layout.IsSidebarCollapsed ? "collapsed" : "shown")}\",\n  \"sidebarWidth\": {WindowLayout.Fit(layout.SidebarWidth)},\n  \"previewShare\": {WindowLayout.FitShare(layout.PreviewShare)}\n}}\n");

    private static bool IsCollapsed(JsonElement root) =>
        root.TryGetProperty("sidebar", out var sidebar)
        && sidebar.ValueKind == JsonValueKind.String
        && sidebar.GetString() == "collapsed";

    private static double Width(JsonElement root) =>
        root.TryGetProperty("sidebarWidth", out var width) && width.ValueKind == JsonValueKind.Number
            ? WindowLayout.Fit(width.GetDouble())
            : WindowLayout.DefaultSidebarWidth;

    private static double Share(JsonElement root) =>
        root.TryGetProperty("previewShare", out var share) && share.ValueKind == JsonValueKind.Number
            ? WindowLayout.FitShare(share.GetDouble())
            : WindowLayout.DefaultPreviewShare;

    private async Task<bool> WriteAsync(WindowLayout layout, CancellationToken cancellationToken)
    {
        var temporary = $"{FilePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(paths.Data);
            await File.WriteAllTextAsync(temporary, Json(layout), cancellationToken);
            File.Move(temporary, FilePath, overwrite: true);

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
