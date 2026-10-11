using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avala.Notifications.Contracts;
using Avala.Sdk;

namespace Avala.Notifications.NotificationFiles;

internal static partial class NotificationFields
{
    public static IReadOnlyDictionary<string, NotificationKind> EventNames { get; } = new Dictionary<string, NotificationKind>(StringComparer.Ordinal)
    {
        ["decision"] = NotificationKind.Decision,
        ["held"] = NotificationKind.Held,
        ["budget"] = NotificationKind.Budget,
        ["failed"] = NotificationKind.Failed,
        ["loopEnded"] = NotificationKind.LoopEnded,
        ["review"] = NotificationKind.Review,
    };

    public static IReadOnlyDictionary<string, DayOfWeek> Weekdays { get; } = new Dictionary<string, DayOfWeek>(StringComparer.Ordinal)
    {
        ["mon"] = DayOfWeek.Monday,
        ["tue"] = DayOfWeek.Tuesday,
        ["wed"] = DayOfWeek.Wednesday,
        ["thu"] = DayOfWeek.Thursday,
        ["fri"] = DayOfWeek.Friday,
        ["sat"] = DayOfWeek.Saturday,
        ["sun"] = DayOfWeek.Sunday,
    };

    public static Result<JsonElement, NotificationError> Only(JsonElement entry, IReadOnlyCollection<string> allowed) =>
        entry.ValueKind != JsonValueKind.Object ? NotificationError.Malformed
        : entry.EnumerateObject().Any(field => !allowed.Contains(field.Name)) ? NotificationError.UnknownField
        : entry;

    public static Result<T, NotificationError> Optional<T>(JsonElement entry, string name, T fallback, Func<JsonElement, Result<T, NotificationError>> read)
        where T : notnull =>
        entry.TryGetProperty(name, out var value) ? read(value) : fallback;

    public static Result<bool, NotificationError> Flag(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => NotificationError.Malformed,
        };

    public static Result<T, NotificationError> Word<T>(JsonElement value, IReadOnlyDictionary<string, T> words, NotificationError unknown)
        where T : notnull =>
        value.ValueKind != JsonValueKind.String ? NotificationError.Malformed
        : words.TryGetValue(value.GetString() ?? string.Empty, out var word) ? word
        : unknown;

    public static Result<NtfyTarget, NotificationError> Ntfy(JsonElement value) =>
        Only(value, ["server", "topic", "tokenEnv"])
            .Bind(_ => Optional(value, "server", NtfyTarget.DefaultServer, Address))
            .Bind(server => Topic(value)
            .Bind(topic => Variable(value, "tokenEnv").Map(token => new NtfyTarget(server, topic) { TokenEnv = token })));

    public static Result<WebhookTarget, NotificationError> Webhook(JsonElement value) =>
        Only(value, ["url", "secretEnv"])
            .Bind(_ => value.TryGetProperty("url", out var url) ? Address(url) : NotificationError.InvalidAddress)
            .Bind(url => Variable(value, "secretEnv").Map(secret => new WebhookTarget(url) { SecretEnv = secret }));

    public static Result<IReadOnlyList<NotificationKind>, NotificationError> Events(JsonElement value)
    {
        if (!Only(value, [.. EventNames.Keys]).TryGetValue(out _, out var error))
        {
            return error;
        }

        var chosen = NotificationSettings.Chosen.ToHashSet();

        foreach (var field in value.EnumerateObject())
        {
            if (field.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return NotificationError.InvalidEvents;
            }

            _ = field.Value.GetBoolean() ? chosen.Add(EventNames[field.Name]) : chosen.Remove(EventNames[field.Name]);
        }

        return Result<IReadOnlyList<NotificationKind>, NotificationError>.Success([.. NotificationSettings.Choosable.Where(chosen.Contains)]);
    }

    public static Result<QuietHours, NotificationError> Quiet(JsonElement value) =>
        Only(value, ["from", "to", "days"])
            .Bind(_ => Time(value, "from"))
            .Bind(from => Time(value, "to")
            .Bind(to => from == to ? NotificationError.InvalidQuietHours : Optional<IReadOnlyList<DayOfWeek>>(value, "days", [], Days)
            .Map(days => new QuietHours(from, to) { Days = days })));

    public static bool IsVariable(string name) => VariablePattern().IsMatch(name);

    public static bool IsTopic(string name) => TopicPattern().IsMatch(name);

    public static Result<Uri, NotificationError> Address(JsonElement value) =>
        value.ValueKind != JsonValueKind.String ? NotificationError.Malformed
        : !Uri.TryCreate(value.GetString(), UriKind.Absolute, out var address) || address.Scheme is not ("https" or "http") || address.UserInfo.Length > 0 || address.Fragment.Length > 0 ? NotificationError.InvalidAddress
        : address.Scheme == "http" && !address.IsLoopback ? NotificationError.InsecureAddress
        : address;

    private static Result<string, NotificationError> Topic(JsonElement value) =>
        !value.TryGetProperty("topic", out var topic) ? NotificationError.InvalidTopic
        : topic.ValueKind != JsonValueKind.String ? NotificationError.Malformed
        : topic.GetString() is { } name && IsTopic(name) ? name
        : NotificationError.InvalidTopic;

    private static Result<Option<string>, NotificationError> Variable(JsonElement value, string field) =>
        !value.TryGetProperty(field, out var variable) ? Option<string>.None
        : variable.ValueKind != JsonValueKind.String ? NotificationError.Malformed
        : variable.GetString() is { } name && IsVariable(name) ? Option<string>.Some(name)
        : NotificationError.InvalidSecret;

    private static Result<TimeOnly, NotificationError> Time(JsonElement value, string field) =>
        !value.TryGetProperty(field, out var time) ? NotificationError.InvalidQuietHours
        : time.ValueKind != JsonValueKind.String ? NotificationError.Malformed
        : TimeOnly.TryParseExact(time.GetString(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at
        : NotificationError.InvalidQuietHours;

    private static Result<IReadOnlyList<DayOfWeek>, NotificationError> Days(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return NotificationError.Malformed;
        }

        var names = value.EnumerateArray().Select(day => day.ValueKind == JsonValueKind.String ? day.GetString() ?? string.Empty : string.Empty).ToList();

        return names.Count == 0 || names.Exists(day => !Weekdays.ContainsKey(day)) || names.Distinct(StringComparer.Ordinal).Count() != names.Count
            ? NotificationError.InvalidQuietHours
            : Result<IReadOnlyList<DayOfWeek>, NotificationError>.Success([.. names.Select(day => Weekdays[day])]);
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex VariablePattern();

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex TopicPattern();
}
