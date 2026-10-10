using Avala.Agents.Contracts.Sessions;

namespace Avala.Observability.Contracts;

public interface IUsageTally
{
    Task TalliedAsync(SessionId session, TurnId turn, CancellationToken cancellationToken);
}
