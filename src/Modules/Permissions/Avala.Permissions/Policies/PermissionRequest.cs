using Avala.Agents.Contracts.Events;
using Avala.Permissions.Contracts;
using Avala.Sdk;

namespace Avala.Permissions.Policies;

internal sealed record PermissionRequest(ItemKind Kind, string Target, bool InsideWorkspace)
{
    public Func<string, PermissionRequest> Locate { get; init; } = path => new PermissionRequest(ItemKind.FileEdit, path, InsideWorkspace: false);
}

internal sealed record Verdict(PolicyAnswer Answer, Option<PolicyRule> Rule);
