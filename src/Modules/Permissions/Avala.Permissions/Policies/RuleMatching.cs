using Avala.Agents.Contracts.Events;
using Avala.Permissions.Contracts;
using Avala.Sdk;

namespace Avala.Permissions.Policies;

internal static class RuleMatching
{
    extension(PolicyRule rule)
    {
        public bool Matches(PermissionRequest request) =>
            rule.Kind.Match(kind => kind == request.Kind, () => true)
            && rule.Target.Match(pattern => rule.Origin == RuleOrigin.Session ? pattern == request.Target : Globs(pattern, request.Target), () => true)
            && rule.Scope switch
            {
                RuleScope.Workspace => request.InsideWorkspace,
                RuleScope.OutsideWorkspace => !request.InsideWorkspace,
                _ => true,
            };

        public bool Covers(PermissionRequest line) =>
            rule.Matches(line) && (rule.Target.IsNone || rule.Origin == RuleOrigin.Session);
    }

    extension(IReadOnlyList<PolicyRule> rules)
    {
        public Verdict First(Func<PolicyRule, bool> matches) =>
            rules.FirstOrDefault(matches) is { } rule ? new Verdict(rule.Answer, rule) : new Verdict(PolicyAnswer.Ask, Option<PolicyRule>.None);

        public Verdict Commanded(PermissionRequest request)
        {
            var line = CommandLine.Parse(request.Target);
            Verdict[] verdicts =
            [
                .. line.Commands.Select(command => rules.First(rule => rule.Covers(request) || rule.Matches(request with { Target = command }))),
                .. line.Writes.Select(path => rules.First(rule => (rule.Origin == RuleOrigin.Session && rule.Covers(request)) || rule.Matches(line.Written(path, request)))),
                .. line.Opaque ? [rules.First(rule => rule.Covers(request))] : Array.Empty<Verdict>(),
            ];

            return verdicts.FirstOrDefault(verdict => verdict.Answer == PolicyAnswer.Deny)
                ?? verdicts.FirstOrDefault(verdict => verdict.Answer == PolicyAnswer.Ask)
                ?? verdicts[0];
        }
    }

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
