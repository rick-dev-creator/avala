using System.Globalization;
using Avala.Agents.Contracts.Sessions;
using Avala.Handoffs.Contracts;
using Avala.Handoffs.Records;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Handoffs.Watching;

internal sealed class RelayMoves(ISessionBoundJobs jobs, HandoffBook book, RelayLedger ledger)
{
    public void RelayAtTurnEnd(SessionId session, Option<DateTimeOffset> until) => ledger.Wind(session, until);

    public async Task WindDownAsync(JobSituation situation, SessionId session, LimitReason why, Option<DateTimeOffset> until, CancellationToken cancellationToken)
    {
        ledger.Wind(session, until);
        var message = new JobMessage(Steer(why)) { Author = MessageAuthor.Avala, CausedBy = MessageAuthor.Avala };

        if ((await jobs.SteerOnAsync(situation.Job, session, message, cancellationToken)).IsFailure)
        {
            await CapAsync(situation, session, why, until, cancellationToken);

            return;
        }

        await book.MarkAsync(new RelayMark(situation.Job, RelayMarkKind.WindingDown, situation.Connection, situation.Situation.Now) { Why = why }, cancellationToken);
    }

    public async Task CapAsync(JobSituation situation, SessionId session, LimitReason why, Option<DateTimeOffset> until, CancellationToken cancellationToken)
    {
        ledger.Settle(session, until);

        if ((await jobs.HoldRunningOnAsync(situation.Job, session, HoldReason.LimitNearlyReached, cancellationToken)).IsSuccess)
        {
            await book.MarkAsync(new RelayMark(situation.Job, RelayMarkKind.Capped, situation.Connection, situation.Situation.Now) { Why = why }, cancellationToken);
        }
    }

    public static string Steer(LimitReason why) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Avala note: the {why.Window} usage window of {why.Connection.Value} is {Math.Round(why.Used * 100)}% spent, so this job moves to another connection when this turn ends. Finish the step you are on and leave the work in a valid state: no half-written files, nothing left broken. Stop any sub-agents you started and start no new ones. Then end your turn with a short note of what is done and what is left.");
}
