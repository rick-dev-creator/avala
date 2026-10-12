using Avala.Agents.Contracts.Events;
using Avala.CommandLines;
using Avala.Permissions.Contracts;
using Avala.Sdk;

namespace Avala.Permissions.Policies;

internal static class Remembering
{
    public const string ForThisJob = "don't ask again for this job";

    public const string InThisRepository = "always in this repository";

    public static PolicyRule ForJob(ItemKind kind, string target, PolicyAnswer answer) =>
        new(RuleOrigin.Job, ForThisJob, kind, target, RuleScope.Anywhere, answer);

    public static PolicyRule Always(ItemKind kind, IReadOnlyList<string> subjects, PolicyAnswer answer, bool contest) =>
        new(RuleOrigin.Job, Named(subjects, contest), kind, string.Join(' ', subjects), RuleScope.Anywhere, answer) { AnyArguments = true };

    public static PolicyRule Contested(PolicyRule rule, bool contest) =>
        contest && rule.Name.EndsWith(" job", StringComparison.Ordinal) ? rule with { Name = $"{rule.Name[..^3]}contest" } : rule;

    private static string Named(IReadOnlyList<string> subjects, bool contest) =>
        $"always {string.Join(", ", subjects)} in this {(contest ? "contest" : "job")}";

    extension(PermissionPolicy policy)
    {
        public JobScope InJob(PermissionRequest request, IReadOnlyList<PolicyRule> kept) =>
            Refusal(request).Match(
                refusal => new JobScope(Option<PolicyRule>.None, refusal, policy.Raisable),
                () => Needed(policy, request, kept, Candidates(request)) is { Count: > 0 } needed
                    ? new JobScope(Always(request.Kind, needed, PolicyAnswer.Allow, contest: false), Option<ScopeRefusal>.None, policy.Raisable)
                    : new JobScope(Option<PolicyRule>.None, ScopeRefusal.StillAsked, policy.Raisable));

        public Option<PolicyRule> InRepository(PermissionRequest request) =>
            Exact(request).Bind(target =>
            {
                var rule = new PolicyRule(RuleOrigin.Repository, InThisRepository, request.Kind, target, RuleScope.Anywhere, PolicyAnswer.Allow);
                var verdict = (policy with { Repository = [.. policy.Repository, rule] }).Decide(request);

                return verdict.Answer == PolicyAnswer.Allow && verdict.Rule == Option<PolicyRule>.Some(rule) ? rule : Option<PolicyRule>.None;
            });
    }

    private static List<string> Needed(PermissionPolicy policy, PermissionRequest request, IReadOnlyList<PolicyRule> kept, IReadOnlyList<string> subjects)
    {
        if (!Allows(policy, request, kept, subjects))
        {
            return [];
        }

        var needed = subjects.ToList();

        foreach (var subject in subjects)
        {
            if (needed.Count > 1 && Allows(policy, request, kept, [.. needed.Where(other => other != subject)]))
            {
                needed.Remove(subject);
            }
        }

        return needed;
    }

    private static bool Allows(PermissionPolicy policy, PermissionRequest request, IReadOnlyList<PolicyRule> kept, IReadOnlyList<string> subjects) =>
        policy.Decide(request, [.. kept, Always(request.Kind, subjects, PolicyAnswer.Allow, contest: false)]).Answer == PolicyAnswer.Allow;

    private static Option<ScopeRefusal> Refusal(PermissionRequest request) =>
        request.Kind == ItemKind.Command
            ? CommandLine.Parse(request.Target).Opaque ? ScopeRefusal.Unanalysable : Option<ScopeRefusal>.None
            : Subjects.Of(request.Kind, request.Target).IsNone ? ScopeRefusal.NoSubject : Option<ScopeRefusal>.None;

    private static List<string> Candidates(PermissionRequest request) =>
        request.Kind == ItemKind.Command
            ? [.. CommandLine.Parse(request.Target).Commands.SelectMany(command => Subjects.Of(ItemKind.Command, command.Text).Match(subject => new[] { subject }, () => [])).Distinct(StringComparer.Ordinal)]
            : [.. Subjects.Of(request.Kind, request.Target).Match(subject => new[] { subject }, () => [])];

    private static Option<string> Exact(PermissionRequest request) =>
        request.Target.Length == 0 || request.Target.AsSpan().IndexOfAny('*', '?') >= 0 ? Option<string>.None
        : request.Kind != ItemKind.Command ? request.Target
        : CommandLine.Parse(request.Target) is { Opaque: false, Commands: [var only] } ? only.Text
        : Option<string>.None;
}
