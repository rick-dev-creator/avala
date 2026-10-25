using System.Globalization;
using Avala.Agents.Contracts.Sessions;
using Avala.Handoffs.Contracts;
using Avala.Handoffs.Records;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Handoffs.Watching;

internal sealed class RelayMoves(IJobs jobs, HandoffBook book, RelayLedger ledger)
{
    public async Task WindDownAsync(JobSituation situation, SessionId session, LimitReason why, CancellationToken cancellationToken)
    {
        ledger.Wind(session);
        var message = new JobMessage(Steer(why)) { Author = MessageAuthor.Avala, CausedBy = MessageAuthor.Avala };

        if ((await jobs.SteerAsync(situation.Job, message, cancellationToken)).IsFailure)
        {
            await CapAsync(situation, session, why, cancellationToken);

            return;
        }

        await book.MarkAsync(new RelayMark(situation.Job, RelayMarkKind.WindingDown, situation.Connection, situation.Situation.Now) { Why = why }, cancellationToken);
    }

    public async Task CapAsync(JobSituation situation, SessionId session, LimitReason why, CancellationToken cancellationToken)
    {
        ledger.Settle(session);

        if ((await jobs.HoldRunningAsync(situation.Job, HoldReason.LimitNearlyReached, cancellationToken)).IsSuccess)
        {
            await book.MarkAsync(new RelayMark(situation.Job, RelayMarkKind.Capped, situation.Connection, situation.Situation.Now) { Why = why }, cancellationToken);
        }
    }

    public static string Steer(LimitReason why) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Avala note: the {why.Window} usage window of {why.Connection.Value} is {Math.Round(why.Used * 100)}% spent, so this job moves to another connection when this turn ends. Finish the step you are on and leave the work in a valid state: no half-written files, nothing left broken. Stop any sub-agents you started and start no new ones. Then end your turn with a short note of what is done and what is left.");
}
