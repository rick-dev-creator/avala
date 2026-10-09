using Avala.Agents.Contracts.Events;
using Avala.Permissions.Contracts;
using Avala.Sdk;

namespace Avala.Permissions.Policies;

internal sealed record PermissionPolicy(IReadOnlyList<PolicyRule> Rules)
{
    public const string PolicyFile = ".avala/permissions.json";

    public static IReadOnlyList<PolicyRule> Guards { get; } =
    [
        new(RuleOrigin.BuiltIn, "policy-file-goes-to-a-human", ItemKind.FileEdit, PolicyFile, RuleScope.Workspace, PolicyAnswer.Ask),
    ];

    public static IReadOnlyList<PolicyRule> Defaults { get; } =
    [
        new(RuleOrigin.BuiltIn, "edits-inside-the-workspace", ItemKind.FileEdit, Option<string>.None, RuleScope.Workspace, PolicyAnswer.Allow),
    ];

    public static PermissionPolicy BuiltIn { get; } = With([]);

    public static PermissionPolicy With(IReadOnlyList<PolicyRule> repository) => new([.. Guards, .. repository, .. Defaults]);

    public Verdict Decide(PermissionRequest request) =>
        Rules.FirstOrDefault(rule => rule.Matches(request)) is { } rule
            ? new Verdict(rule.Answer, rule)
            : new Verdict(PolicyAnswer.Ask, Option<PolicyRule>.None);
}
