using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Observability.Contracts;

public sealed record UsageSession(SessionId Session, ProviderInfo Provider, Option<AgentAccount> Account, ConnectionName Connection, DateTimeOffset Opened)
{
    public Option<JobId> Job { get; init; }
}

public interface IUsageSessions
{
    IReadOnlyList<UsageSession> Sessions();
}
