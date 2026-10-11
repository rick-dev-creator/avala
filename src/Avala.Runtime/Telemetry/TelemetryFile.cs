using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avala.Sdk;

namespace Avala.Runtime.Telemetry;

internal sealed record TelemetryRecord(bool Enabled, Option<string> InstallId, bool NoticeShown, bool InstalledSent, Option<DateOnly> ActiveOn)
{
    public static TelemetryRecord Fresh { get; } = new(true, Option<string>.None, false, false, Option<DateOnly>.None);

    public static string NewInstallId() => Guid.NewGuid().ToString("D");
}

internal sealed class TelemetryFile(AvalaPaths paths)
{
    public const string FileName = "telemetry.json";

    public const int MaximumBytes = 16 * 1024;

    private const string DateFormat = "yyyy-MM-dd";

    private string FilePath => Path.Combine(paths.Data, FileName);

    public async Task<Option<TelemetryRecord>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(FilePath))
        {
            return TelemetryRecord.Fresh;
        }

        try
        {
            await using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);

            if (stream.Length > MaximumBytes)
            {
                return Option<TelemetryRecord>.None;
            }

            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            return document.RootElement.ValueKind == JsonValueKind.Object ? Read(document.RootElement) : Option<TelemetryRecord>.None;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException)
        {
            return Option<TelemetryRecord>.None;
        }
    }

    public async Task<bool> WriteAsync(TelemetryRecord record, CancellationToken cancellationToken)
    {
        var temporary = $"{FilePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(paths.Data);
            await File.WriteAllTextAsync(temporary, Text(record), cancellationToken);
            File.Move(temporary, FilePath, overwrite: true);

            return true;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static Option<TelemetryRecord> Read(JsonElement root) =>
        Flag(root, "enabled", absent: true) is { } enabled
            ? new TelemetryRecord(
                enabled,
                Text(root, "installId") is { Length: > 0 and <= 36 } id ? id : Option<string>.None,
                Flag(root, "noticeShown", absent: false) ?? false,
                Flag(root, "installedSent", absent: false) ?? false,
                Text(root, "activeOn") is { } day && DateOnly.TryParseExact(day, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var on)
                    ? on
                    : Option<DateOnly>.None)
            : Option<TelemetryRecord>.None;

    private static bool? Flag(JsonElement root, string name, bool absent) =>
        !root.TryGetProperty(name, out var value) ? absent
        : value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Text(TelemetryRecord record)
    {
        var root = new JsonObject
        {
            ["enabled"] = record.Enabled,
            ["installId"] = record.InstallId.Match<JsonNode?>(id => id, () => null),
            ["noticeShown"] = record.NoticeShown,
            ["installedSent"] = record.InstalledSent,
            ["activeOn"] = record.ActiveOn.Match<JsonNode?>(day => day.ToString(DateFormat, CultureInfo.InvariantCulture), () => null),
        };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
    }
}
