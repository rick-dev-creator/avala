using System.Text;
using System.Text.Json;
using Avala.Notifications.Contracts;
using Avala.Sdk;

namespace Avala.Notifications.NotificationFiles;

internal static class NotificationFileParser
{
    public const int MaximumBytes = 16 * 1024;

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 4, AllowDuplicateProperties = false };

    private static readonly string[] Fields = ["enabled", "channel", "ntfy", "webhook", "events", "detail", "quietHours"];

    private static readonly Dictionary<string, ChannelKind> Channels = new(StringComparer.Ordinal) { ["ntfy"] = ChannelKind.Ntfy, ["webhook"] = ChannelKind.Webhook };

    private static readonly Dictionary<string, DetailLevel> Details = new(StringComparer.Ordinal) { ["minimal"] = DetailLevel.Minimal, ["detailed"] = DetailLevel.Detailed };

    public static Result<NotificationSettings, NotificationError> Parse(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaximumBytes)
        {
            return NotificationError.TooLarge;
        }

        try
        {
            using var document = JsonDocument.Parse(text, Options);

            return document.RootElement.ValueKind == JsonValueKind.Object ? Settings(document.RootElement) : NotificationError.Malformed;
        }
        catch (JsonException)
        {
            return NotificationError.Malformed;
        }
    }

    private static Result<NotificationSettings, NotificationError> Settings(JsonElement root) =>
        NotificationFields.Only(root, Fields)
            .Bind(_ => NotificationFields.Optional(root, "channel", ChannelKind.Ntfy, value => NotificationFields.Word(value, Channels, NotificationError.InvalidChannel)))
            .Bind(channel => NotificationFields.Optional(root, "enabled", true, NotificationFields.Flag)
            .Bind(enabled => Section(root, "ntfy", NotificationFields.Ntfy)
            .Bind(ntfy => Section(root, "webhook", NotificationFields.Webhook)
            .Bind(webhook => NotificationFields.Optional(root, "events", NotificationSettings.Chosen, NotificationFields.Events)
            .Bind(events => NotificationFields.Optional(root, "detail", DetailLevel.Minimal, value => NotificationFields.Word(value, Details, NotificationError.InvalidDetail))
            .Bind(detail => Section(root, "quietHours", NotificationFields.Quiet)
            .Bind(quiet => Chosen(new NotificationSettings(channel)
            {
                Enabled = enabled,
                Ntfy = ntfy,
                Webhook = webhook,
                Events = events,
                Detail = detail,
                Quiet = quiet,
            }))))))));

    private static Result<NotificationSettings, NotificationError> Chosen(NotificationSettings settings) =>
        (settings.Channel == ChannelKind.Ntfy ? settings.Ntfy.IsSome : settings.Webhook.IsSome) ? settings : NotificationError.MissingChannel;

    private static Result<Option<T>, NotificationError> Section<T>(JsonElement root, string name, Func<JsonElement, Result<T, NotificationError>> read)
        where T : notnull =>
        root.TryGetProperty(name, out var value) ? read(value).Map(Option<T>.Some) : Option<T>.None;
}
