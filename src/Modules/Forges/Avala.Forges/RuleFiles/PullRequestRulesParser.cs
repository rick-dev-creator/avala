using System.Text;
using System.Text.Json;
using Avala.Forges.Contracts;
using Avala.Forges.Policy;
using Avala.Sdk;

namespace Avala.Forges.RuleFiles;

internal static class PullRequestRulesParser
{
    public const string JobFile = ".avala/jobs.json";

    public const string Section = "pullRequest";

    public const int MaximumBytes = 16 * 1024;

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 3, AllowDuplicateProperties = false };

    public static Result<Option<PullRequestRules>, ForgeError> ParseJobFile(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaximumBytes)
        {
            return ForgeError.TooLarge;
        }

        try
        {
            using var document = JsonDocument.Parse(text, Options);
            var root = document.RootElement;

            return root.ValueKind != JsonValueKind.Object ? ForgeError.Malformed
                : root.TryGetProperty(Section, out var section) ? Rules(section).Map(Option<PullRequestRules>.Some)
                : Result<Option<PullRequestRules>, ForgeError>.Success(Option<PullRequestRules>.None);
        }
        catch (JsonException)
        {
            return ForgeError.Malformed;
        }
    }

    public static Result<PullRequestRules, ForgeError> Rules(JsonElement section)
    {
        if (section.ValueKind != JsonValueKind.Object)
        {
            return ForgeError.Malformed;
        }

        if (section.EnumerateObject().Any(field => field.Name is not ("forge" or "remote" or "onPullRequest" or "maxWakeUps")))
        {
            return ForgeError.UnknownField;
        }

        return Named(section, "forge", ForgeNames.IsValid).Bind(forge => forge.Match(
            named => Named(section, "remote", remote => remote.Length > 0).Bind(remote => Policy(section).Bind(policy => WakeUps(section)
                .Map(wakeUps => new PullRequestRules(new ForgeName(named), remote.Match(given => given, () => PullRequestRules.DefaultRemote), policy, wakeUps)))),
            () => ForgeError.MissingForge));
    }

    private static Result<Option<string>, ForgeError> Named(JsonElement section, string field, Func<string, bool> valid) =>
        !section.TryGetProperty(field, out var value) ? Option<string>.None
        : value.ValueKind == JsonValueKind.String && value.GetString() is { } text && valid(text) ? Option<string>.Some(text)
        : ForgeError.Malformed;

    private static Result<OnPullRequest, ForgeError> Policy(JsonElement section) =>
        !section.TryGetProperty("onPullRequest", out var policy) ? OnPullRequest.WatchOnly
        : policy.ValueKind != JsonValueKind.String ? ForgeError.Malformed
        : policy.GetString() switch
        {
            "watch-only" => OnPullRequest.WatchOnly,
            "wake-on-ci" => OnPullRequest.WakeOnCi,
            "wake-on-ci-and-reviews" => OnPullRequest.WakeOnCiAndReviews,
            _ => ForgeError.UnknownPolicy,
        };

    private static Result<int, ForgeError> WakeUps(JsonElement section) =>
        !section.TryGetProperty("maxWakeUps", out var wakeUps) ? PullRequestRules.DefaultWakeUps
        : wakeUps.ValueKind != JsonValueKind.Number ? ForgeError.Malformed
        : wakeUps.TryGetInt32(out var count) && count is >= 0 and <= PullRequestRules.MostWakeUps ? count
        : ForgeError.InvalidWakeUps;
}
