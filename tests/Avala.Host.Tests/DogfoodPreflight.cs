using System.Globalization;
using System.Text.Json.Nodes;
using Avala.Sdk;
using static Avala.Host.Tests.DogfoodSettings;

namespace Avala.Host.Tests;

internal sealed record DogfoodAccount(string Connection, string Folder, bool LoggedIn, string Email, string Account, Option<double> FiveHours, Option<double> SevenDays, Option<DateTimeOffset> Read)
{
    public Option<double> ExtraUsage { get; init; }

    public Option<double> Used =>
        FiveHours.Match(five => SevenDays.Match(seven => Math.Max(five, seven), () => five), () => SevenDays);

    public Option<double> Lowest =>
        FiveHours.Match(five => SevenDays.Match(seven => Math.Min(five, seven), () => five), () => SevenDays);

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Connection} ({Folder}): logged in {LoggedIn}, {Email}, account {Account}, 5h {Show(FiveHours)}, 7d {Show(SevenDays)}, extra usage {Show(ExtraUsage)}, read {Read.Match(at => at.ToString("u", CultureInfo.InvariantCulture), () => "never")}");

    private static string Show(Option<double> used) => used.Match(value => value.ToString("P0", CultureInfo.InvariantCulture), () => "unknown");
}

internal sealed record DogfoodReadiness(string Version, IReadOnlyList<DogfoodAccount> Accounts, IReadOnlyList<string> Refusals, double Threshold);

internal static class DogfoodPreflight
{
    public static async Task<DogfoodReadiness> CheckAsync(DogfoodScenario scenario, DogfoodJournal journal)
    {
        var (_, version) = await DogfoodRepository.RunAsync("claude", ["--version"], new Dictionary<string, string?>());
        var main = await AccountAsync(Main, MainLogin);
        var exhausted = scenario.UsesExhausted ? [await AccountAsync(Exhausted, ExhaustedLogin)] : Array.Empty<DogfoodAccount>();
        DogfoodAccount[] accounts = [main, .. exhausted];
        var threshold = Threshold.Match(chosen => chosen, () => main.Lowest.Match(lowest => Math.Floor(lowest * 90) / 100, () => 0d));
        List<string> refusals =
        [
            .. Usable(main),
            .. exhausted.SelectMany(account => Spent(account, main)),
            .. scenario.Thresholded && threshold <= 0
                ? [$"{Main} has no cached window reading to derive the limit threshold from; set AVALA_DOGFOOD_THRESHOLD"]
                : Array.Empty<string>(),
        ];

        foreach (var account in accounts)
        {
            await journal.NoteAsync($"PREFLIGHT {account}");
        }

        await journal.NoteAsync($"PREFLIGHT {version.Trim()}; threshold for limit scenarios {threshold.ToString(CultureInfo.InvariantCulture)}; refusals [{string.Join("; ", refusals)}]");

        return new DogfoodReadiness(version.Trim(), accounts, refusals, threshold);
    }

    private static IEnumerable<string> Usable(DogfoodAccount account)
    {
        if (!account.LoggedIn)
        {
            yield return $"{account.Connection} is not logged in";
        }

        if (account.Used.Match(fraction => fraction >= Headroom, () => false))
        {
            yield return string.Create(CultureInfo.InvariantCulture, $"{account.Connection} has no headroom: a window is at {account.Used.Match(fraction => fraction, () => 0):P0}, at or over {Headroom:P0}");
        }
    }

    private static IEnumerable<string> Spent(DogfoodAccount account, DogfoodAccount main)
    {
        if (!account.LoggedIn)
        {
            yield return $"{account.Connection} is not logged in";
        }

        if (!account.Used.Match(fraction => fraction >= 1, () => false))
        {
            yield return string.Create(CultureInfo.InvariantCulture, $"{account.Connection} is not at its usage limit: its most used window is at {account.Used.Match(fraction => fraction.ToString("P0", CultureInfo.InvariantCulture), () => "an unknown reading")}");
        }

        if (!ExtraUsageAllowed && account.ExtraUsage.Match(fraction => fraction < 1, () => false))
        {
            yield return string.Create(CultureInfo.InvariantCulture, $"{account.Connection} has extra usage at {account.ExtraUsage.Match(fraction => fraction, () => 0):P0}, so past its limit it may be billed instead of refused; turn extra usage off or set AVALA_DOGFOOD_ALLOW_EXTRA_USAGE=1");
        }

        if (account.Email.Length > 0 && account.Email == main.Email && account.Account == main.Account)
        {
            yield return $"{Exhausted} and {Main} are the same account";
        }
    }

    private static async Task<DogfoodAccount> AccountAsync(string connection, string folder)
    {
        var (code, output) = await DogfoodRepository.RunAsync("claude", ["auth", "status", "--json"], new Dictionary<string, string?> { ["CLAUDE_CONFIG_DIR"] = folder });
        var status = code == 0 ? Parse(output) : null;
        var cached = await CachedAsync(folder);
        var utilization = cached?["utilization"];

        return new DogfoodAccount(
            connection,
            folder,
            status?["loggedIn"]?.GetValue<bool>() == true,
            status?["email"]?.GetValue<string>() ?? string.Empty,
            cached?["accountUuid"]?.GetValue<string>() ?? status?["orgId"]?.GetValue<string>() ?? string.Empty,
            Window(utilization, "five_hour"),
            Window(utilization, "seven_day"),
            cached?["fetchedAtMs"] is { } fetched ? DateTimeOffset.FromUnixTimeMilliseconds(fetched.GetValue<long>()) : Option<DateTimeOffset>.None)
        {
            ExtraUsage = Window(utilization, "extra_usage"),
        };
    }

    private static Option<double> Window(JsonNode? utilization, string name) => Fraction(utilization?[name]?["utilization"]);

    private static async Task<JsonNode?> CachedAsync(string folder)
    {
        var path = Path.Combine(folder, ".claude.json");

        return File.Exists(path) ? Parse(await File.ReadAllTextAsync(path, Cancellation))?["cachedUsageUtilization"] : null;
    }

    private static Option<double> Fraction(JsonNode? percent) =>
        percent is JsonValue value && value.TryGetValue<double>(out var used) ? used / 100 : Option<double>.None;

    private static JsonNode? Parse(string text)
    {
        try
        {
            return JsonNode.Parse(text);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
