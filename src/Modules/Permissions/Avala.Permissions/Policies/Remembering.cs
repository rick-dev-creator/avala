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

    extension(PermissionPolicy policy)
    {
        public Option<PolicyRule> InRepository(PermissionRequest request) =>
            Exact(request).Bind(target =>
            {
                var rule = new PolicyRule(RuleOrigin.Repository, InThisRepository, request.Kind, target, RuleScope.Anywhere, PolicyAnswer.Allow);
                var verdict = (policy with { Repository = [.. policy.Repository, rule] }).Decide(request);

                return verdict.Answer == PolicyAnswer.Allow && verdict.Rule == Option<PolicyRule>.Some(rule) ? rule : Option<PolicyRule>.None;
            });
    }

    private static Option<string> Exact(PermissionRequest request) =>
        request.Target.Length == 0 || request.Target.AsSpan().IndexOfAny('*', '?') >= 0 ? Option<string>.None
        : request.Kind != ItemKind.Command ? request.Target
        : CommandLine.Parse(request.Target) is { Opaque: false, Commands: [var only] } ? only.Text
        : Option<string>.None;
}
