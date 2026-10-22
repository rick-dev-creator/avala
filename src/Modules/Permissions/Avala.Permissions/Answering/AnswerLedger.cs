using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Permissions.Governance;
using Avala.Sdk.Events;

namespace Avala.Permissions.Answering;

internal sealed class AnswerLedger(GovernanceBook book, IEventBus bus, TimeProvider clock, IJobRounds rounds)
{
    public ValueTask JoinedAsync(SessionId session, CancellationToken cancellationToken) => rounds.PersonJoinedAsync(session, cancellationToken);

    public async Task<HumanAnswer> RecordAsync(HumanAnswer answer, CancellationToken cancellationToken)
    {
        var recorded = answer with { At = clock.GetUtcNow() };
        await book.RecordAsync(recorded, cancellationToken);
        await bus.PublishAsync(new PermissionAnswered(recorded), cancellationToken);

        return recorded;
    }

    public bool Raise(JobId job) => book.Raise(job, clock.GetUtcNow());

    public void Lower(JobId job) => book.Lower(job);

    public async Task RaisedAsync(JobId job, CancellationToken cancellationToken)
    {
        foreach (var autonomy in await book.RaisedAsync(job, cancellationToken))
        {
            await bus.PublishAsync(new AutonomyApplied(autonomy), cancellationToken);
        }
    }

    public async Task ReplacedAsync(PolicyDecision decision, CancellationToken cancellationToken)
    {
        await book.ReplacedAsync(decision, cancellationToken);
        await bus.PublishAsync(new PermissionDecided(decision), cancellationToken);
    }

    public async Task ReplacedAsync(FormDecision decision, CancellationToken cancellationToken)
    {
        await book.ReplacedAsync(decision, cancellationToken);
        await bus.PublishAsync(new FormDecided(decision), cancellationToken);
    }
}
