using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;

namespace Avala.Permissions.Tests;

internal sealed class RecordedRounds : IJobRounds
{
    public List<SessionId> Joined { get; } = [];

    public ValueTask PersonJoinedAsync(SessionId session, CancellationToken cancellationToken)
    {
        Joined.Add(session);

        return ValueTask.CompletedTask;
    }
}
