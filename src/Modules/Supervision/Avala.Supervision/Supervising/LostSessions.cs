using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Supervision.Supervising;

internal sealed class LostSessions(SupervisionBook book, Intervener intervener) : IHandle<SessionEnded>
{
    public async ValueTask HandleAsync(SessionEnded integrationEvent, CancellationToken cancellationToken) =>
        await book.JobOf(integrationEvent.Session)
            .Bind(job => book.Watch(job).Session == Option<SessionId>.Some(integrationEvent.Session) ? job : Option<JobId>.None)
            .Match(
                job => intervener.LostAsync(job, integrationEvent.Ending, cancellationToken),
                () => Task.CompletedTask);
}
