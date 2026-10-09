using System.Text;
using System.Text.Json;
using Avala.Autopilot.Approving;
using Avala.Autopilot.Contracts;
using Avala.Autopilot.Evidence;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Autopilot.RepositoryFiles;

internal sealed class AutopilotRulesReader(IBaseFiles files) : IAutopilotRules
{
    public const string JobFile = ".avala/jobs.json";

    public const int MaximumBytes = 16 * 1024;

    private const string Section = "autopilot";

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 2, AllowDuplicateProperties = false };

    public async Task<Result<AutopilotRules, AutopilotError>> OfWorktreeAsync(string worktree, CancellationToken cancellationToken) =>
        (await files.ReadAsync(worktree, JobFile, cancellationToken)).Match(
            file => file.Content.Match(Parse, () => AutopilotRules.Default),
            _ => AutopilotError.Unreadable);

    public static Result<AutopilotRules, AutopilotError> Parse(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaximumBytes)
        {
            return AutopilotError.TooLarge;
        }

        try
        {
            using var document = JsonDocument.Parse(text, Options);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return AutopilotError.Malformed;
            }

            return root.TryGetProperty(Section, out var section) ? Declared(section) : AutopilotRules.Default;
        }
        catch (JsonException)
        {
            return AutopilotError.Malformed;
        }
    }

    private static Result<AutopilotRules, AutopilotError> Declared(JsonElement section)
    {
        if (section.ValueKind != JsonValueKind.Object)
        {
            return AutopilotError.Malformed;
        }

        if (section.EnumerateObject().Any(property => property.Name is not ("approve" or "followUps")))
        {
            return AutopilotError.UnknownField;
        }

        return Rule(section, "approve", ApprovalRule.Never, new Dictionary<string, ApprovalRule>
            {
                ["never"] = ApprovalRule.Never,
                ["cleanEvidence"] = ApprovalRule.CleanEvidence,
            })
            .Bind(approve => Rule(section, "followUps", FollowUpRule.Refuse, new Dictionary<string, FollowUpRule>
                {
                    ["refuse"] = FollowUpRule.Refuse,
                    ["accept"] = FollowUpRule.Accept,
                })
                .Map(followUps => new AutopilotRules(approve, followUps)));
    }

    private static Result<TRule, AutopilotError> Rule<TRule>(JsonElement section, string field, TRule fallback, Dictionary<string, TRule> names)
        where TRule : struct, Enum
    {
        if (!section.TryGetProperty(field, out var declared))
        {
            return fallback;
        }

        if (declared.ValueKind != JsonValueKind.String)
        {
            return AutopilotError.Malformed;
        }

        return names.TryGetValue(declared.GetString() ?? string.Empty, out var rule) ? rule : AutopilotError.UnknownRule;
    }
}
