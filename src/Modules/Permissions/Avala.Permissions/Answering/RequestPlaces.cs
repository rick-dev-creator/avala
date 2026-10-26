using Avala.Agents.Contracts.Events;
using Avala.Permissions.Contracts;
using Avala.Permissions.Policies;
using Avala.Sdk;

namespace Avala.Permissions.Answering;

internal sealed class RequestPlaces(IRealPaths paths, IReadableFolders readable)
{
    public async ValueTask<PermissionRequest> FactsAsync(GovernedSession session, PermissionRequested requested, IReadOnlyList<PolicyRule> jobRules, CancellationToken cancellationToken)
    {
        var granted = Grants(jobRules, Option<ItemKind>.None);
        var folders = requested.Reads.IsSome || requested.Kind == ItemKind.Command ? [.. await readable.FoldersAsync(cancellationToken), .. granted] : granted;
        var read = requested.Reads.IsSome ? [.. folders, .. session.Own, .. session.Writable] : folders;

        return requested.Facts(session.WorkingDirectory, session.Writable, read, Grants(jobRules, ItemKind.FileEdit), paths);
    }

    public static string[] Grants(IReadOnlyList<PolicyRule> jobRules, Option<ItemKind> kind) =>
        [.. jobRules
            .Where(rule => rule.Scope == RuleScope.Granted && rule.Answer == PolicyAnswer.Allow && (kind.IsNone || rule.Kind == kind))
            .SelectMany(rule => rule.Target.Match(folder => new[] { folder }, () => []))
            .Distinct(StringComparer.Ordinal)];
}
