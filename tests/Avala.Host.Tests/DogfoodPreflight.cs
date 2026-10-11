using System.Globalization;
using System.Text.Json.Nodes;
using Avala.Sdk;
using static Avala.Host.Tests.DogfoodSettings;

namespace Avala.Host.Tests;

internal sealed record DogfoodAccount(string Connection, string Folder, bool LoggedIn, string Email, string Account, Option<double> FiveHours, Option<double> SevenDays, Option<DateTimeOffset> Read)
{
    public Option<double> Used =>
        FiveHours.Match(five => SevenDays.Match(seven => Math.Max(five, seven), () => five), () => SevenDays);

    public Option<double> Lowest =>
        FiveHours.Match(five => SevenDays.Match(seven => Math.Min(five, seven), () => five), () => SevenDays);

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Connection} ({Folder}): logged in {LoggedIn}, {Email}, account {Account}, 5h {Show(FiveHours)}, 7d {Show(SevenDays)}, read {Read.Match(at => at.ToString("u", CultureInfo.InvariantCulture), () => "never")}");

    private static string Show(Option<double> used) => used.Match(value => value.ToString("P0", CultureInfo.InvariantCulture), () => "unknown");
}

internal sealed record DogfoodReadiness(string Version, IReadOnlyList<DogfoodAccount> Accounts, IReadOnlyList<string> Refusals, double Threshold);

internal static class DogfoodPreflight
{
    public static async Task<DogfoodReadiness> CheckAsync(DogfoodScenario scenario, DogfoodJournal journal)
    {
        var (_, version) = await DogfoodRepository.RunAsync("claude", ["--version"], new Dictionary<string, string?>());
        var main = await AccountAsync(Main, MainLogin);
        var other = await AccountAsync(Other, OtherLogin);
        DogfoodAccount[] accounts = [main, other];
        var used = scenario.UsesOther ? accounts : [main];
        List<string> refusals =
        [
            .. used.Where(account => !account.LoggedIn).Select(account => $"{account.Connection} is not logged in"),
            .. used.Where(account => account.Used.Match(fraction => fraction >= Headroom, () => false))
                .Select(account => string.Create(CultureInfo.InvariantCulture, $"{account.Connection} has no headroom: a window is at {account.Used.Match(fraction => fraction, () => 0):P0}, at or over {Headroom:P0}")),
            .. scenario.UsesOther && main.Email.Length > 0 && main.Email == other.Email && main.Account == other.Account
                ? [$"{Main} and {Other} are the same account"]
                : Array.Empty<string>(),
        ];
        var threshold = Threshold.Match(chosen => chosen, () => main.Lowest.Match(lowest => Math.Floor(lowest * 90) / 100, () => 0d));

        foreach (var account in accounts)
        {
            await journal.NoteAsync($"PREFLIGHT {account}");
        }

        await journal.NoteAsync($"PREFLIGHT {version.Trim()}; threshold for limit scenarios {threshold.ToString(CultureInfo.InvariantCulture)}; refusals [{string.Join("; ", refusals)}]");

        return new DogfoodReadiness(version.Trim(), accounts, refusals, threshold);
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
            Fraction(utilization?["five_hour"]?["utilization"]),
            Fraction(utilization?["seven_day"]?["utilization"]),
            cached?["fetchedAtMs"] is { } fetched ? DateTimeOffset.FromUnixTimeMilliseconds(fetched.GetValue<long>()) : Option<DateTimeOffset>.None);
    }

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
