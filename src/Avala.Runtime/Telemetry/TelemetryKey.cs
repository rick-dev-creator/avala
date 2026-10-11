using Avala.Sdk;

namespace Avala.Runtime.Telemetry;

internal sealed record TelemetryKey(string AppKey, Uri Events)
{
    public static Uri Europe { get; } = new("https://eu.aptabase.com");

    public static Uri UnitedStates { get; } = new("https://us.aptabase.com");

    public static Option<TelemetryKey> Parse(string key, Option<string> selfHosted)
    {
        var text = key.Trim();

        return text.Split('-') is ["A", var region, { Length: > 0 } id] && id.All(char.IsAsciiLetterOrDigit)
            ? region switch
            {
                "EU" => new TelemetryKey(text, EventsAt(Europe)),
                "US" => new TelemetryKey(text, EventsAt(UnitedStates)),
                "SH" => selfHosted.Bind(Host).Map(host => new TelemetryKey(text, EventsAt(host))),
                _ => Option<TelemetryKey>.None,
            }
            : Option<TelemetryKey>.None;
    }

    private static Option<Uri> Host(string text) =>
        Uri.TryCreate(text.Trim(), UriKind.Absolute, out var host) && (host.Scheme == Uri.UriSchemeHttps || host.Scheme == Uri.UriSchemeHttp)
            ? host
            : Option<Uri>.None;

    private static Uri EventsAt(Uri host) => new($"{host.AbsoluteUri.TrimEnd('/')}/api/v0/event");
}
