using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;

namespace Avala.Permissions.Policies;

internal sealed record PermissionPolicy(IReadOnlyList<PolicyRule> Repository, Autonomy Declared, FormStrategy Strategy)
{
    public const string PolicyFile = ".avala/permissions.json";

    public static IReadOnlyList<PolicyRule> Guards { get; } =
    [
        new(RuleOrigin.BuiltIn, "policy-file-goes-to-a-human", ItemKind.FileEdit, PolicyFile, RuleScope.Workspace, PolicyAnswer.Ask),
        new(RuleOrigin.BuiltIn, "edits-outside-the-workspace-go-to-a-human", ItemKind.FileEdit, Option<string>.None, RuleScope.OutsideWorkspace, PolicyAnswer.Ask),
    ];

    public static IReadOnlyList<PolicyRule> Defaults { get; } =
    [
        new(RuleOrigin.BuiltIn, "edits-inside-the-workspace", ItemKind.FileEdit, Option<string>.None, RuleScope.Workspace, PolicyAnswer.Allow),
    ];

    public static IReadOnlyList<PolicyRule> Unattended { get; } =
    [
        new(RuleOrigin.BuiltIn, "autonomous-commands-in-the-worktree", ItemKind.Command, Option<string>.None, RuleScope.Anywhere, PolicyAnswer.Allow),
        new(RuleOrigin.BuiltIn, "autonomous-denies-the-rest", Option<ItemKind>.None, Option<string>.None, RuleScope.Anywhere, PolicyAnswer.Deny),
    ];

    public static PermissionPolicy BuiltIn { get; } = With([]);

    public Option<Autonomy> Ceiling { get; init; }

    public Autonomy Autonomy => Ceiling.Match(ceiling => ceiling < Declared ? ceiling : Declared, () => Declared);

    public IReadOnlyList<PolicyRule> Rules => Ordered([]);

    public static PermissionPolicy With(IReadOnlyList<PolicyRule> repository) => new(repository, Autonomy.Supervised, FormStrategy.Recommended);

    public PermissionPolicy Capped(Option<Autonomy> requested) => this with { Ceiling = requested };

    public Verdict Decide(PermissionRequest request) => Decide(request, []);

    public Verdict Decide(PermissionRequest request, IReadOnlyList<PolicyRule> session)
    {
        var verdict = Ordered(session).FirstOrDefault(rule => rule.Matches(request)) is { } rule
            ? new Verdict(rule.Answer, rule)
            : new Verdict(PolicyAnswer.Ask, Option<PolicyRule>.None);

        return Autonomy == Autonomy.Autonomous && verdict.Answer == PolicyAnswer.Ask ? verdict with { Answer = PolicyAnswer.Deny } : verdict;
    }

    private List<PolicyRule> Ordered(IReadOnlyList<PolicyRule> session) =>
        [.. Guards, .. Repository, .. session, .. Defaults, .. Autonomy == Autonomy.Autonomous ? Unattended : []];
}
