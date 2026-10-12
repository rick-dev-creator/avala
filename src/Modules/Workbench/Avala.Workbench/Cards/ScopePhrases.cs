using Avala.Agents.Contracts.Events;
using Avala.Permissions.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Cards;

internal static class ScopePhrases
{
    public const string Once = "Once";

    public const string Supervised = "repository is supervised";

    public static string Always(ItemKind kind, Option<PolicyRule> rule, bool contest) =>
        $"Always {rule.Bind(found => found.Target).Match(target => Subjects(kind, target), () => Unnamed(kind))} in this {Where(contest)}";

    public static string Everything(bool contest) => $"Everything in this {Where(contest)}";

    public static string AlwaysUnavailable(ItemKind kind, Option<JobScope> scope) =>
        scope.Bind(found => found.Refusal).Match(refusal => Refused(kind, refusal), () => string.Empty);

    public static string EverythingUnavailable(Option<JobScope> scope) =>
        scope.Match(found => found.Raisable ? string.Empty : Supervised, () => string.Empty);

    private static string Subjects(ItemKind kind, string target) =>
        kind == ItemKind.Command ? string.Join(", ", target.Split(' ')) : target;

    private static string Unnamed(ItemKind kind) => kind switch
    {
        ItemKind.Command => "this program",
        ItemKind.Web => "this host",
        ItemKind.FileEdit => "this file",
        _ => "this tool",
    };

    private static string Refused(ItemKind kind, ScopeRefusal refusal) => refusal switch
    {
        ScopeRefusal.Unanalysable => "can't tell what this line runs",
        ScopeRefusal.NoSubject => kind == ItemKind.Web ? "no single host to remember" : "nothing to remember",
        _ => "a rule still asks for this",
    };

    private static string Where(bool contest) => contest ? "contest" : "job";
}
