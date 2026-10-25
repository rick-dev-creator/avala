namespace Avala.Server.Activity.Domain;

internal static class UsageQuarter
{
    public static TimeSpan Length { get; } = TimeSpan.FromMinutes(15);

    public static DateTimeOffset Of(EventTime at)
    {
        var instant = at.At.ToUniversalTime();

        return new DateTimeOffset(instant.Ticks - (instant.Ticks % Length.Ticks), TimeSpan.Zero);
    }
}
