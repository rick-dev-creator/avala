using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;

namespace Avala.Permissions.Policies;

internal sealed record PermissionPolicy(IReadOnlyList<PolicyRule> Repository, Autonomy Declared, FormStrategy Strategy)
{
    public const string PolicyFile = ".avala/permissions.json";

    public const string NotesFolder = ".avala/notes/*";

    public static IReadOnlyList<PolicyRule> Guards { get; } =
    [
        new(RuleOrigin.BuiltIn, "policy-file-goes-to-a-human", ItemKind.FileEdit, PolicyFile, RuleScope.Workspace, PolicyAnswer.Ask),
        new(RuleOrigin.BuiltIn, "notes-belong-to-the-developer", ItemKind.FileEdit, NotesFolder, RuleScope.Workspace, PolicyAnswer.Deny),
        new(RuleOrigin.BuiltIn, "edits-outside-the-workspace-go-to-a-human", ItemKind.FileEdit, Option<string>.None, RuleScope.OutsideWorkspace, PolicyAnswer.Ask),
        new(RuleOrigin.BuiltIn, "git-files-go-to-a-human", ItemKind.FileEdit, ".git", RuleScope.Workspace, PolicyAnswer.Ask),
        new(RuleOrigin.BuiltIn, "git-folder-goes-to-a-human", ItemKind.FileEdit, ".git/*", RuleScope.Workspace, PolicyAnswer.Ask),
    ];

    public const string ReadOnlyAllowed = "read-only allowed";

    public const string ReadableAllowed = "reads-in-readable-folders";

    public static IReadOnlyList<PolicyRule> Defaults { get; } =
    [
        new(RuleOrigin.BuiltIn, "edits-inside-the-workspace", ItemKind.FileEdit, Option<string>.None, RuleScope.Workspace, PolicyAnswer.Allow),
        new(RuleOrigin.BuiltIn, ReadOnlyAllowed, ItemKind.Command, Option<string>.None, RuleScope.ReadOnly, PolicyAnswer.Allow),
        new(RuleOrigin.BuiltIn, ReadableAllowed, Option<ItemKind>.None, Option<string>.None, RuleScope.Readable, PolicyAnswer.Allow),
    ];

    public static IReadOnlyList<PolicyRule> Unattended { get; } =
    [
        new(RuleOrigin.BuiltIn, "autonomous-commands-in-the-worktree", ItemKind.Command, Option<string>.None, RuleScope.Anywhere, PolicyAnswer.Allow),
        new(RuleOrigin.BuiltIn, "autonomous-denies-the-rest", Option<ItemKind>.None, Option<string>.None, RuleScope.Anywhere, PolicyAnswer.Deny),
    ];

    public static PolicyRule ReadOnlyGuard { get; } =
        new(RuleOrigin.BuiltIn, "read-only-denies-edits", ItemKind.FileEdit, Option<string>.None, RuleScope.Anywhere, PolicyAnswer.Deny);

    public static PermissionPolicy BuiltIn { get; } = With([]);

    public Option<Autonomy> Ceiling { get; init; }

    public bool ReadOnly { get; init; }

    public Autonomy Limit { get; init; } = Autonomy.Autonomous;

    public bool Raised { get; init; }

    public bool Raisable => Limit == Autonomy.Autonomous && Autonomy == Autonomy.Supervised;

    public Autonomy Autonomy => Raised ? Limit : Ceiling.Match(ceiling => ceiling < Declared ? ceiling : Declared, () => Declared);

    public IReadOnlyList<PolicyRule> Rules => Ordered([]);

    public static PermissionPolicy With(IReadOnlyList<PolicyRule> repository) => new(repository, Autonomy.Supervised, FormStrategy.Recommended);

    public PermissionPolicy Capped(Option<Autonomy> requested) => this with { Ceiling = requested };

    public Verdict Decide(PermissionRequest request) => Decide(request, []);

    public Verdict Decide(PermissionRequest request, IReadOnlyList<PolicyRule> session)
    {
        var rules = Ordered(session);
        var verdict = request.Kind == ItemKind.Command ? rules.Commanded(request) : rules.First(rule => rule.Matches(request));

        return Autonomy == Autonomy.Autonomous && verdict.Answer == PolicyAnswer.Ask ? verdict with { Answer = PolicyAnswer.Deny } : verdict;
    }

    private List<PolicyRule> Ordered(IReadOnlyList<PolicyRule> session) =>
        [.. ReadOnly ? [ReadOnlyGuard] : Array.Empty<PolicyRule>(), .. Guards, .. Repository, .. session, .. Defaults, .. Autonomy == Autonomy.Autonomous ? UnattendedRules : []];

    private IEnumerable<PolicyRule> UnattendedRules => ReadOnly ? Unattended.Where(rule => rule.Kind != Option<ItemKind>.Some(ItemKind.Command)) : Unattended;
}
