using Avala.Permissions.Contracts;

namespace Avala.Permissions.Policies;

internal static class RuleMatching
{
    extension(PolicyRule rule)
    {
        public bool Matches(PermissionRequest request) =>
            rule.Kind.Match(kind => kind == request.Kind, () => true)
            && rule.Target.Match(pattern => Globs(pattern, request.Target), () => true)
            && (rule.Scope == RuleScope.Anywhere || request.InsideWorkspace);
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
