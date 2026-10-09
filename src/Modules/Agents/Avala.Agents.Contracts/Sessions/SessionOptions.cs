using Avala.Agents.Contracts.Connections;
using Avala.Sdk;

namespace Avala.Agents.Contracts.Sessions;

public sealed record SessionOptions(string WorkingDirectory, PermissionMode Permissions)
{
    public Option<ResumeToken> Resume { get; init; }

    public IReadOnlyList<HarnessTool> Tools { get; init; } = [];

    public ConnectionEnvironment Connection { get; init; } = ConnectionEnvironment.Default;
}
