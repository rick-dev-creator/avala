using Avala.Agents.Contracts.Events;
using Avala.Permissions.Contracts;
using Avala.Sdk;

namespace Avala.Permissions.Policies;

internal sealed record PermissionRequest(ItemKind Kind, string Target, bool InsideWorkspace)
{
    public Func<string, PermissionRequest> Locate { get; init; } = path => new PermissionRequest(ItemKind.FileEdit, path, InsideWorkspace: false);

    public bool ReadOnly { get; init; }

    public bool Readable { get; init; }

    public Option<string> Granted { get; init; }

    public Option<string> Grantable { get; init; }
}

internal sealed record Verdict(PolicyAnswer Answer, Option<PolicyRule> Rule);
