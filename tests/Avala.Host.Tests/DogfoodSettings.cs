using System.Globalization;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Host.Tests;

internal static class DogfoodSettings
{
    internal const string Gate = "AVALA_DOGFOOD";

    internal const string Main = "claude";

    internal const string Lead = "claude-lead";

    internal const string Exhausted = "claude-work";

    internal const string HookSecret = "AVALA_DOGFOOD_HOOK_SECRET";


    internal static readonly JobStatus[] Settled = [JobStatus.AwaitingReview, JobStatus.NeedsHelp, JobStatus.Failed, JobStatus.Approved, JobStatus.Discarded];

    internal static readonly string[] Refused = ["sudo", "curl ", "wget ", "ssh ", "scp ", "git push", "git remote", "npx ", "pip ", "pip3 ", "npm install", "-g ", "--global", "http.server", "live-server", "npm start", "npm run dev", "npm run serve", "serve -", "nohup", "&>/dev/null &", " & "];

    internal const string SlowCheck = "sleep 21";

    internal static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    internal static bool Gated => Environment.GetEnvironmentVariable(Gate) == "1";

    internal static bool Rehearsing => Environment.GetEnvironmentVariable("AVALA_DOGFOOD_REHEARSAL") == "1";

    internal static string Selected => Environment.GetEnvironmentVariable("AVALA_DOGFOOD_SCENARIO") ?? string.Empty;

    internal static string Root => Environment.GetEnvironmentVariable("AVALA_DOGFOOD_ROOT") is { Length: > 0 } folder
        ? folder
        : Path.Combine(Path.GetTempPath(), "avala-dogfood");

    internal static string Campaign => Environment.GetEnvironmentVariable("AVALA_DOGFOOD_CAMPAIGN") is { Length: > 0 } folder
        ? folder
        : Path.Combine(Root, Rehearsing ? "rehearsal" : "campaign");

    internal static string MainLogin => Folder("AVALA_DOGFOOD_LOGIN", ".claude");

    internal static string ExhaustedLogin => Folder("AVALA_DOGFOOD_EXHAUSTED_LOGIN", ".claude-work");

    internal static bool ExtraUsageAllowed => Environment.GetEnvironmentVariable("AVALA_DOGFOOD_ALLOW_EXTRA_USAGE") == "1";

    internal static decimal CampaignCap => Amount("AVALA_DOGFOOD_CAMPAIGN_CAP", "3.00");

    internal static double Headroom => double.Parse(Environment.GetEnvironmentVariable("AVALA_DOGFOOD_HEADROOM") is { Length: > 0 } value ? value : "0.90", CultureInfo.InvariantCulture);

    internal static Option<double> Threshold =>
        Environment.GetEnvironmentVariable("AVALA_DOGFOOD_THRESHOLD") is { Length: > 0 } value ? double.Parse(value, CultureInfo.InvariantCulture) : Option<double>.None;

    internal static bool Chosen(string scenario, bool optional) =>
        Selected.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is var names
        && (names.Contains(scenario, StringComparer.OrdinalIgnoreCase) || (!optional && names.Contains("all", StringComparer.OrdinalIgnoreCase)));

    private static decimal Amount(string variable, string fallback) =>
        decimal.Parse(Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value ? value : fallback, CultureInfo.InvariantCulture);

    private static string Folder(string variable, string fallback) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } folder
            ? folder
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), fallback);
}
