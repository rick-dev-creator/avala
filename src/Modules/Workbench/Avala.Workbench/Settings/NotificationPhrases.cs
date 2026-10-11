using System.Globalization;
using Avala.Notifications.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Settings;

internal static class NotificationPhrases
{
    public const string SetUpHint =
        "Avala can tell your phone when a job needs you while you are away from this window. Set up creates notifications.json with a private, random ntfy.sh topic: subscribe to it in the ntfy app.";

    public const string TestSent = "Test sent. It should reach every device subscribed to the topic.";

    public static string Status(NotificationFile file) => file.Status switch
    {
        NotificationFileStatus.Absent => "not set up",
        NotificationFileStatus.Applied => file.Settings.Match(settings => settings.Enabled ? "applied" : "applied, turned off", () => "applied"),
        _ => $"rejected: {file.Error.Match(Error, () => "invalid")}",
    };

    public static string Channel(NotificationSettings settings) => settings.Channel == ChannelKind.Ntfy ? "ntfy" : "Webhook";

    public static string Destination(NotificationSettings settings) =>
        settings.Channel == ChannelKind.Ntfy
            ? settings.Ntfy.Match(target => $"{target.Server.OriginalString.TrimEnd('/')}/{target.Topic}", () => string.Empty)
            : settings.Webhook.Match(target => target.Url.OriginalString, () => string.Empty);

    public static string Secret(NotificationSettings settings, SecretStatus status) =>
        settings.SecretVariable.Match(
            variable => status == SecretStatus.Set ? $"{variable} is set" : $"{variable} is not set: nothing is sent until it is",
            () => settings.Channel == ChannelKind.Ntfy
                ? "No token: anyone who knows the topic can read what is sent"
                : "Unsigned: name a secretEnv to sign each request");

    public static string Quiet(Option<QuietHours> quiet) =>
        quiet.Match(
            hours => string.Create(
                CultureInfo.InvariantCulture,
                $"{hours.From:HH\\:mm}–{hours.To:HH\\:mm} {(hours.Days.Count == 0 ? "every day" : string.Join(", ", hours.Days.Select(day => CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedDayName(day))))}, urgent notices still sent, the rest in one digest after"),
            () => "None");

    public static string Last(Option<NotificationRecord> last, TimeZoneInfo zone) =>
        last.Match(
            record => record.Outcome == NotificationOutcome.Sent
                ? $"Sent {Kind(record.Kind)} at {Local(record.At, zone)}"
                : $"Failed to send {Kind(record.Kind)} at {Local(record.At, zone)} after {Attempts(record.Attempts)}: {record.Error.Match(Error, () => "unknown error")}",
            () => "Nothing sent yet");

    public static string Error(NotificationError error) => error switch
    {
        NotificationError.Unreadable => "notifications.json could not be read",
        NotificationError.TooLarge => "notifications.json is over 16 KiB",
        NotificationError.Malformed => "notifications.json is not valid JSON of the expected shape",
        NotificationError.UnknownField => "notifications.json has a field Avala does not know",
        NotificationError.InvalidChannel => "the channel must be ntfy or webhook",
        NotificationError.MissingChannel => "the chosen channel has no section",
        NotificationError.InvalidAddress => "an address is not an https URL",
        NotificationError.InsecureAddress => "plain http is allowed only on this computer",
        NotificationError.InvalidTopic => "the topic must be 1 to 64 letters, digits, - or _",
        NotificationError.InvalidSecret => "a variable name is not valid",
        NotificationError.InvalidEvents => "events must be true or false",
        NotificationError.InvalidDetail => "detail must be minimal or detailed",
        NotificationError.InvalidQuietHours => "quiet hours need distinct from and to times, HH:mm, and valid days",
        NotificationError.Unwritable => "notifications.json could not be written",
        NotificationError.AlreadySetUp => "notifications.json already exists",
        NotificationError.NotSetUp => "notifications are not set up",
        NotificationError.Disabled => "notifications are turned off",
        NotificationError.MissingSecret => "the variable holding the secret is not set",
        NotificationError.Unreachable => "the server could not be reached",
        _ => "the server refused the request",
    };

    public static string Sentence(NotificationError error)
    {
        var text = Error(error);

        return $"{char.ToUpperInvariant(text[0])}{text[1..]}.";
    }

    private static string Kind(NotificationKind kind) => kind switch
    {
        NotificationKind.Decision => "a decision",
        NotificationKind.Held => "a held job",
        NotificationKind.Budget => "a budget hold",
        NotificationKind.Failed => "a failed job",
        NotificationKind.LoopEnded => "an ended loop",
        NotificationKind.Review => "a job ready for review",
        NotificationKind.Summary => "a summary",
        NotificationKind.Digest => "a digest",
        _ => "a test",
    };

    private static string Attempts(int attempts) => attempts == 1 ? "1 attempt" : $"{attempts} attempts";

    private static string Local(DateTimeOffset at, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(at, zone).ToString("HH:mm", CultureInfo.InvariantCulture);
}
