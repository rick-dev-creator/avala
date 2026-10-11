using System.Globalization;
using System.Text;
using System.Text.Json;
using Avala.Notifications.Contracts;
using Avala.Sdk;

namespace Avala.Notifications.NotificationFiles;

internal static class NotificationText
{
    private static readonly JsonWriterOptions Indented = new() { Indented = true };

    public static string Write(NotificationSettings settings)
    {
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, Indented))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("enabled", settings.Enabled);
            writer.WriteString("channel", settings.Channel == ChannelKind.Ntfy ? "ntfy" : "webhook");
            Each(settings.Ntfy, ntfy => Ntfy(writer, ntfy));
            Each(settings.Webhook, webhook => Webhook(writer, webhook));
            Events(writer, settings.Events);
            writer.WriteString("detail", settings.Detail == DetailLevel.Detailed ? "detailed" : "minimal");
            Each(settings.Quiet, quiet => Quiet(writer, quiet));
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    private static void Ntfy(Utf8JsonWriter writer, NtfyTarget ntfy)
    {
        writer.WriteStartObject("ntfy");
        writer.WriteString("server", ntfy.Server.OriginalString);
        writer.WriteString("topic", ntfy.Topic);
        Each(ntfy.TokenEnv, token => writer.WriteString("tokenEnv", token));
        writer.WriteEndObject();
    }

    private static void Webhook(Utf8JsonWriter writer, WebhookTarget webhook)
    {
        writer.WriteStartObject("webhook");
        writer.WriteString("url", webhook.Url.OriginalString);
        Each(webhook.SecretEnv, secret => writer.WriteString("secretEnv", secret));
        writer.WriteEndObject();
    }

    private static void Events(Utf8JsonWriter writer, IReadOnlyList<NotificationKind> events)
    {
        writer.WriteStartObject("events");

        foreach (var (name, kind) in NotificationFields.EventNames)
        {
            writer.WriteBoolean(name, events.Contains(kind));
        }

        writer.WriteEndObject();
    }

    private static void Quiet(Utf8JsonWriter writer, QuietHours quiet)
    {
        writer.WriteStartObject("quietHours");
        writer.WriteString("from", quiet.From.ToString("HH:mm", CultureInfo.InvariantCulture));
        writer.WriteString("to", quiet.To.ToString("HH:mm", CultureInfo.InvariantCulture));

        if (quiet.Days.Count > 0)
        {
            writer.WriteStartArray("days");

            foreach (var day in quiet.Days)
            {
                writer.WriteStringValue(NotificationFields.Weekdays.First(named => named.Value == day).Key);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private static void Each<T>(Option<T> value, Action<T> write)
        where T : notnull
    {
        foreach (var present in value.Match<T[]>(found => [found], () => []))
        {
            write(present);
        }
    }
}
