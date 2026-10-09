using System.Text.Json;
using Avala.Sdk;
using Avala.Sdk.Appearance;
using Avala.Sdk.Events;

namespace Avala.Runtime.Appearance;

internal sealed class AppearanceFile(AvalaPaths paths, IEventBus bus) : IAppearance, IStartupTask, IAsyncDisposable
{
    public const string FileName = "appearance.json";

    public const int MaximumBytes = 16 * 1024;

    private readonly SerialExecutor writes = new();
    private Task<AppearanceSettings>? loading;

    private string FilePath => Path.Combine(paths.Data, FileName);

    public async ValueTask<AppearanceSettings> ReadAsync(CancellationToken cancellationToken) =>
        await LazyInitializer.EnsureInitialized(ref loading, () => LoadAsync(FilePath)).WaitAsync(cancellationToken);

    public async ValueTask<Result<AppearanceSettings, AppearanceError>> ChangeAsync(AppearancePreference preference, CancellationToken cancellationToken)
    {
        var written = await writes.RunAsync(token => WriteAsync(preference, token), cancellationToken);

        if (written.IsSuccess)
        {
            await bus.PublishAsync(new AppearanceChanged(preference), cancellationToken);
        }

        return written;
    }

    public async Task RunAsync(CancellationToken cancellationToken) =>
        await bus.PublishAsync(new AppearanceChanged((await ReadAsync(cancellationToken)).Preference), cancellationToken);

    public ValueTask DisposeAsync() => writes.DisposeAsync();

    private async Task<Result<AppearanceSettings, AppearanceError>> WriteAsync(AppearancePreference preference, CancellationToken cancellationToken)
    {
        var temporary = $"{FilePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(paths.Data);
            await File.WriteAllTextAsync(temporary, AppearanceText.Write(preference), cancellationToken);
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return AppearanceError.Unwritable;
        }

        var settings = new AppearanceSettings(preference, AppearanceFileStatus.Applied, Option<AppearanceError>.None);
        Volatile.Write(ref loading, Task.FromResult(settings));

        return settings;
    }

    private static async Task<AppearanceSettings> LoadAsync(string path) =>
        !File.Exists(path)
            ? new AppearanceSettings(AppearancePreference.Default, AppearanceFileStatus.Absent, Option<AppearanceError>.None)
            : (await ReadTextAsync(path)).Bind(AppearanceText.Read).Match(
                preference => new AppearanceSettings(preference, AppearanceFileStatus.Applied, Option<AppearanceError>.None),
                error => new AppearanceSettings(AppearancePreference.Default, AppearanceFileStatus.Rejected, error));

    private static async Task<Result<string, AppearanceError>> ReadTextAsync(string path)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);

            if (stream.Length > MaximumBytes)
            {
                return AppearanceError.TooLarge;
            }

            using var reader = new StreamReader(stream);

            return await reader.ReadToEndAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return AppearanceError.Unreadable;
        }
    }
}

internal static class AppearanceText
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static Result<AppearancePreference, AppearanceError> Read(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);

            return document.RootElement.ValueKind == JsonValueKind.Object
                ? Preference(document.RootElement)
                : AppearanceError.Invalid;
        }
        catch (JsonException)
        {
            return AppearanceError.Invalid;
        }
    }

    public static string Write(AppearancePreference preference) =>
        JsonSerializer.Serialize(
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["theme"] = preference.Theme.ToString().ToLowerInvariant(),
                ["reduceMotion"] = preference.Motion switch
                {
                    MotionChoice.Reduced => "on",
                    MotionChoice.Full => "off",
                    _ => "system",
                },
            },
            Indented) + "\n";

    private static Result<AppearancePreference, AppearanceError> Preference(JsonElement root)
    {
        var theme = root.TryGetProperty("theme", out var named) ? Theme(named) : ThemeChoice.System;
        var motion = root.TryGetProperty("reduceMotion", out var reduce) ? Motion(reduce) : MotionChoice.System;

        return theme is { } chosen && motion is { } reduced
            ? new AppearancePreference(chosen, reduced)
            : AppearanceError.Invalid;
    }

    private static ThemeChoice? Theme(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 } text
        && text.All(char.IsAsciiLetter)
        && Enum.TryParse<ThemeChoice>(text, ignoreCase: true, out var theme)
            ? theme
            : null;

    private static MotionChoice? Motion(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => MotionChoice.Reduced,
        JsonValueKind.False => MotionChoice.Full,
        JsonValueKind.String => value.GetString() switch
        {
            "system" => MotionChoice.System,
            "on" => MotionChoice.Reduced,
            "off" => MotionChoice.Full,
            _ => null,
        },
        _ => null,
    };
}
