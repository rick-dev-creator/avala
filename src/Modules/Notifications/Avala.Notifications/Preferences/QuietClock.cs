using Avala.Notifications.Contracts;
using Avala.Sdk;

namespace Avala.Notifications.Preferences;

internal static class QuietClock
{
    public static Option<DateTimeOffset> EndOf(Option<QuietHours> quiet, DateTimeOffset now, TimeZoneInfo zone) =>
        quiet.Bind(hours => EndOf(hours, now, zone));

    public static Option<DateTimeOffset> EndOf(QuietHours hours, DateTimeOffset now, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone).DateTime;

        foreach (var day in new[] { local.Date, local.Date.AddDays(-1) })
        {
            var start = day + hours.From.ToTimeSpan();
            var end = (hours.From < hours.To ? day : day.AddDays(1)) + hours.To.ToTimeSpan();

            if ((hours.Days.Count == 0 || hours.Days.Contains(day.DayOfWeek)) && start <= local && local < end)
            {
                return Moment(end, zone);
            }
        }

        return Option<DateTimeOffset>.None;
    }

    private static DateTimeOffset Moment(DateTime local, TimeZoneInfo zone)
    {
        var valid = zone.IsInvalidTime(local) ? local.AddHours(1) : local;

        return new DateTimeOffset(valid, zone.GetUtcOffset(valid));
    }
}
