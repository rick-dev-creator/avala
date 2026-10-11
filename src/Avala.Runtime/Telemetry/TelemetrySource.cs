using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avala.Sdk;

namespace Avala.Runtime.Telemetry;

internal sealed record TelemetryPlatform(string OsName, string OsVersion, string Architecture)
{
    public static TelemetryPlatform Current { get; } = new(
        OperatingSystem.IsWindows() ? "Windows"
        : OperatingSystem.IsMacOS() ? "macOS"
        : OperatingSystem.IsLinux() ? "Linux"
        : "Other",
        Environment.OSVersion.Version.ToString(),
        RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant());
}

internal sealed record TelemetrySource(AvalaBuild Build, TelemetryPlatform Platform, Option<string> OptedOutBy)
{
    public const string Installed = "installed";

    public const string Active = "active";

    public const string DoNotTrack = "DO_NOT_TRACK";

    public const string AvalaTelemetry = "AVALA_TELEMETRY";

    public const string Sender = "avala";

    private static readonly string[] Refusals = ["0", "false", "off", "no"];

    public static TelemetrySource Of(AvalaBuild build, Func<string, string?> environment) =>
        new(build, TelemetryPlatform.Current, OptOut(environment));

    public static Option<string> OptOut(Func<string, string?> environment) =>
        environment(DoNotTrack)?.Trim() is { Length: > 0 } track && !Refusals.Contains(track, StringComparer.OrdinalIgnoreCase)
            ? $"{DoNotTrack}={track}"
            : environment(AvalaTelemetry)?.Trim() is { Length: > 0 } avala && Refusals.Contains(avala, StringComparer.OrdinalIgnoreCase)
                ? $"{AvalaTelemetry}={avala}"
                : Option<string>.None;

    public string Payload(string eventName, string installId, DateTimeOffset at)
    {
        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("timestamp", at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
            writer.WriteString("sessionId", installId);
            writer.WriteString("eventName", eventName);
            writer.WriteStartObject("systemProps");
            writer.WriteString("osName", Platform.OsName);
            writer.WriteString("osVersion", Platform.OsVersion);
            writer.WriteString("appVersion", Build.Version);
            writer.WriteString("sdkVersion", Sender);
            writer.WriteEndObject();
            writer.WriteStartObject("props");
            writer.WriteString("architecture", Platform.Architecture);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
