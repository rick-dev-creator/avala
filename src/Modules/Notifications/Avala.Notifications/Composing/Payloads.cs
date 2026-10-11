using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avala.Notifications.Contracts;

namespace Avala.Notifications.Composing;

internal sealed record Payload(Uri Address, string Body);

internal static class Payloads
{
    public const string TimestampHeader = "X-Avala-Timestamp";

    public const string NonceHeader = "X-Avala-Nonce";

    public const string SignatureHeader = "X-Avala-Signature";

    private const int Urgent = 4;

    private const int Ordinary = 3;

    public static Payload Ntfy(OutgoingMessage message, NtfyTarget target) =>
        new(new Uri($"{target.Server.AbsoluteUri.TrimEnd('/')}/"), Json(writer =>
        {
            writer.WriteString("topic", target.Topic);
            writer.WriteString("title", message.Title);
            writer.WriteString("message", message.Body);
            writer.WriteNumber("priority", message.Severity == Severity.Urgent ? Urgent : Ordinary);
            writer.WriteStartArray("tags");
            writer.WriteStringValue("avala");
            writer.WriteStringValue(EventName(message.Kind));
            writer.WriteEndArray();
        }));

    public static Payload Webhook(OutgoingMessage message, WebhookTarget target) =>
        new(target.Url, Json(writer =>
        {
            writer.WriteString("event", EventName(message.Kind));

            if (message.Job.Match(job => job.Value.ToString("D"), () => string.Empty) is { Length: > 0 } job)
            {
                writer.WriteString("job", job);
            }
            else
            {
                writer.WriteNull("job");
            }

            writer.WriteString("repository", message.Repository);
            writer.WriteString("title", message.Title);
            writer.WriteString("body", message.Body);
            writer.WriteNumber("count", message.Count);
            writer.WriteString("severity", message.Severity == Severity.Urgent ? "urgent" : "info");
            writer.WriteString("at", message.At.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            writer.WriteNull("link");
        }));

    public static string EventName(NotificationKind kind) => JsonNamingPolicy.CamelCase.ConvertName(kind.ToString());

    public static string Signature(string secret, string timestamp, string nonce, string body) =>
        "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{nonce}.{body}")));

    private static string Json(Action<Utf8JsonWriter> fields)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            fields(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
