using Avala.Agents.Contracts.Events;
using Avala.CommandLines;
using Avala.Permissions.Contracts;
using Avala.Sdk;

namespace Avala.Permissions.Policies;

internal static class RuleMatching
{
    extension(PolicyRule rule)
    {
        public bool Matches(PermissionRequest request) =>
            rule.Kind.Match(kind => kind == request.Kind, () => true)
            && rule.Target.Match(pattern => Targets(rule, pattern, request), () => true)
            && rule.Scope switch
            {
                RuleScope.Workspace => request.InsideWorkspace,
                RuleScope.OutsideWorkspace => !request.InsideWorkspace,
                RuleScope.ReadOnly => request.ReadOnly,
                _ => true,
            };

        public bool Covers(PermissionRequest line) =>
            rule.Matches(line) && (rule.Target.IsNone || (rule.Answered && !rule.AnyArguments));

        public bool Answered => rule.Origin is RuleOrigin.Session or RuleOrigin.Job;
    }

    extension(IReadOnlyList<PolicyRule> rules)
    {
        public Verdict First(Func<PolicyRule, bool> matches) =>
            rules.FirstOrDefault(matches) is { } rule ? new Verdict(rule.Answer, rule) : new Verdict(PolicyAnswer.Ask, Option<PolicyRule>.None);

        public Verdict Commanded(PermissionRequest request)
        {
            var line = CommandLine.Parse(request.Target);
            var reads = line.Writes.Count == 0 && !line.Opaque && !line.MovesDirectory;
            Verdict[] verdicts =
            [
                .. line.Commands.Select(command => rules.First(rule => rule.Covers(request) || rule.Matches(Part(request, command, reads)))),
                .. line.Writes.Select(path => rules.First(rule => (rule.Answered && rule.Covers(request)) || rule.Matches(Written(line, path, request)))),
                .. line.Opaque ? [rules.First(rule => rule.Covers(request))] : Array.Empty<Verdict>(),
            ];

            return verdicts.FirstOrDefault(verdict => verdict.Answer == PolicyAnswer.Deny)
                ?? verdicts.FirstOrDefault(verdict => verdict.Answer == PolicyAnswer.Ask)
                ?? verdicts[0];
        }
    }

    private static bool Targets(PolicyRule rule, string pattern, PermissionRequest request) =>
        rule.AnyArguments ? Subjects.Of(request.Kind, request.Target).Match(subject => Names(rule, pattern, subject), () => false)
        : rule.Answered ? pattern == request.Target
        : Globs(pattern, request.Target);

    private static bool Names(PolicyRule rule, string pattern, string subject) =>
        rule.Kind == Option<ItemKind>.Some(ItemKind.Command) ? pattern.Split(' ').Contains(subject, StringComparer.Ordinal) : pattern == subject;

    private static PermissionRequest Part(PermissionRequest request, ShellCommand command, bool reads) =>
        request with { Target = command.Text, ReadOnly = reads && ReadOnlyCommands.Reads(command, request.Locate) };

    private static PermissionRequest Written(CommandLine line, string path, PermissionRequest request) =>
        line.MovesDirectory && !CommandLine.Rooted(path) ? new PermissionRequest(ItemKind.FileEdit, path, InsideWorkspace: false) : request.Locate(path);

    private static bool Globs(string pattern, string text)
    {
        var (p, t, star, resume) = (0, 0, -1, 0);

        while (t < text.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || pattern[p] == text[t]))
            {
                (p, t) = (p + 1, t + 1);
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                (star, resume, p) = (p, t, p + 1);
            }
            else if (star >= 0)
            {
                (p, resume) = (star + 1, resume + 1);
                t = resume;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }
}
