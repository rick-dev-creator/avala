using System.Globalization;

namespace Avala.Server.Activity.Domain;

internal sealed record UsageRequest(DateOnly From, DateOnly To, string Zone, UsageStep Step, IReadOnlyList<UsagePeriod> Periods, RepositoryKey? Repository);

internal sealed record UsageRefusal(string Field, string Reason);

internal sealed record UsageReading(UsageRequest? Request, IReadOnlyList<UsageRefusal> Refusals);

internal static class UsageRequests
{
    private static readonly Dictionary<string, UsageStep> Steps = new(StringComparer.Ordinal)
    {
        ["day"] = UsageStep.Day,
        ["week"] = UsageStep.Week,
        ["month"] = UsageStep.Month,
    };

    public static UsageReading Read(string? from, string? to, string? zone, string? step, string? repository)
    {
        List<UsageRefusal> refusals = [];
        var start = Date(from, "From", refusals);
        var end = Date(to, "To", refusals);
        var found = TimeZone(zone, refusals);
        var stepped = Step(step, refusals);
        var key = repository is null ? null : RepositoryKey.Create(repository);

        if (key?.Refusal is { } refused)
        {
            refusals.Add(new UsageRefusal("Repository", refused));
        }

        if (refusals.Count > 0)
        {
            return new UsageReading(null, refusals);
        }

        var periods = UsagePeriods.Of(start, end, found!, stepped);

        return periods.Refusal is { } cut
            ? new UsageReading(null, [new UsageRefusal("To", cut)])
            : new UsageReading(new UsageRequest(start, end, found!.Id, stepped, periods.Value, key?.Value), []);
    }

    private static DateOnly Date(string? text, string field, List<UsageRefusal> refusals)
    {
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        refusals.Add(new UsageRefusal(field, $"the field '{field.ToLowerInvariant()}' must be a date such as 2026-10-19"));

        return default;
    }

    private static TimeZoneInfo? TimeZone(string? zone, List<UsageRefusal> refusals)
    {
        if (zone is { Length: > 0 } && TimeZoneInfo.TryFindSystemTimeZoneById(zone, out var found) && found.HasIanaId)
        {
            return found;
        }

        refusals.Add(new UsageRefusal("Zone", "the field 'zone' must be an IANA time zone such as Europe/Madrid"));

        return null;
    }

    private static UsageStep Step(string? step, List<UsageRefusal> refusals)
    {
        if (step is not null && Steps.TryGetValue(step, out var found))
        {
            return found;
        }

        refusals.Add(new UsageRefusal("Step", "the field 'step' must be day, week or month"));

        return UsageStep.Day;
    }
}
