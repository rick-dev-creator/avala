using System.Collections.Immutable;
using Avala.Notifications.Composing;
using Avala.Notifications.Contracts;
using Avala.Notifications.Pacing;
using Avala.Notifications.Preferences;
using Avala.Notifications.Records;
using Avala.Sdk;
using Avala.Sdk.Diagnostics;
using Avala.Sdk.Presence;

namespace Avala.Notifications.Dispatching;

internal sealed class Outlook(INotificationFile file, IPresence presence, IRedaction redaction, TimeProvider clock)
{
    public DateTimeOffset Now => clock.GetUtcNow();

    public async Task<Option<NotificationSettings>> SettingsAsync(CancellationToken cancellationToken) =>
        (await file.ReadAsync(cancellationToken)).Settings.Bind(settings => settings.Enabled ? Option<NotificationSettings>.Some(settings) : Option<NotificationSettings>.None);

    public Circumstances Circumstances(NotificationSettings settings, ImmutableHashSet<string> sent, bool capped)
    {
        var now = Now;

        return new(
            presence.IsAwayFor(Pacing.Circumstances.AwayAfter, now),
            presence.LastSeen + Pacing.Circumstances.AwayAfter,
            QuietUntil(settings),
            sent,
            capped);
    }

    public Option<DateTimeOffset> QuietUntil(NotificationSettings settings) => QuietClock.EndOf(settings.Quiet, Now, clock.LocalTimeZone);

    public string Redact(string text) => redaction.Apply(text);

    public string Preview(NotificationSettings settings) => Previews.Of(settings, Redact, Now);

    public ITimer Alarm(Action ring, DateTimeOffset due) => clock.CreateAlarm(ring, due);
}
