using Avala.Agents.Contracts.Connections;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Agents.Contracts.Sessions;

public sealed record SessionOptions(string WorkingDirectory, PermissionMode Permissions)
{
    public Option<ResumeToken> Resume { get; init; }

    public ModelChoice Model { get; init; } = ModelChoice.Default;

    public IReadOnlyList<HarnessTool> Tools { get; init; } = [];

    public ConnectionEnvironment Connection { get; init; } = ConnectionEnvironment.Default;

    public IProcessLauncher Processes { get; init; } = UncontainedProcesses.Instance;
}
