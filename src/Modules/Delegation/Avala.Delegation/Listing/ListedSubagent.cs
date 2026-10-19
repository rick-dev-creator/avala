using Avala.Agents.Contracts.Connections;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Listing;

internal enum SubagentKind
{
    InProcess,
    CrossHarness,
}

internal sealed record ListedSubagent(string Name, SubagentKind Kind, SubagentState State, string Status)
{
    public Option<JobId> Job { get; init; }

    public Option<ConnectionName> Connection { get; init; }

    public Option<string> Harness { get; init; }

    public Option<ChildRole> Role { get; init; }
}
