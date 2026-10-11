using Avala.Forges.Contracts;
using Avala.Sdk;
using Avala.Triggers.Contracts;

namespace Avala.Forges.Sifting;

internal sealed record EventJudgement(ForgeEventVerdict Verdict, Option<FilterReason> Reason)
{
    public static EventJudgement Fires { get; } = new(ForgeEventVerdict.Fired, Option<FilterReason>.None);

    public static EventJudgement NotAllowed { get; } = new(ForgeEventVerdict.NotAllowed, Option<FilterReason>.None);

    public bool Passes => Verdict == ForgeEventVerdict.Fired;

    public static EventJudgement Filtered(FilterReason reason) => new(ForgeEventVerdict.Filtered, reason);
}

internal static class EventFilter
{
    public static EventJudgement Judge(ForgeRule rule, ForgeEvent happened) =>
        happened.FromFork ? EventJudgement.Filtered(FilterReason.FromFork)
        : Mismatch(rule, happened).Match(EventJudgement.Filtered, () => Allowed(rule, happened.Actor) ? EventJudgement.Fires : EventJudgement.NotAllowed);

    public static bool Allowed(ForgeRule rule, string actor) =>
        actor.Length > 0 && rule.Authors.Any(author => string.Equals(author, actor, StringComparison.OrdinalIgnoreCase));

    public static bool Commands(string command, string comment)
    {
        var line = comment.TrimStart().Split('\n', 2)[0].TrimEnd();

        return line.StartsWith(command, StringComparison.OrdinalIgnoreCase) && (line.Length == command.Length || char.IsWhiteSpace(line[command.Length]));
    }

    private static Option<FilterReason> Mismatch(ForgeRule rule, ForgeEvent happened) =>
        rule.Event switch
        {
            ForgeEventKind.IssueLabeled when !Same(rule.Label, happened.Label) => FilterReason.OtherLabel,
            ForgeEventKind.CommentCommand when !rule.Command.Match(command => Commands(command, Text(happened.Comment)), () => false) => FilterReason.OtherCommand,
            ForgeEventKind.ReviewRequested when rule.Reviewer.IsSome && !Same(rule.Reviewer, happened.Reviewer) => FilterReason.OtherReviewer,
            _ => Option<FilterReason>.None,
        };

    private static bool Same(Option<string> wanted, Option<string> found) =>
        string.Equals(Text(wanted), Text(found), StringComparison.OrdinalIgnoreCase) && wanted.IsSome;

    private static string Text(Option<string> text) => text.Match(found => found, () => string.Empty);
}
