using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Handoffs.Contracts;
using Avala.Handoffs.Policy;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Handoffs.Watching;

internal sealed class WindDown(Situations situations, IUsage usage, RelayMoves moves, RelayLedger ledger) : IHandle<UsageRecorded>
{
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(1);

    private readonly Dictionary<SessionId, DateTimeOffset> looked = [];

    public async ValueTask HandleAsync(UsageRecorded integrationEvent, CancellationToken cancellationToken)
    {
        var session = integrationEvent.Session;

        if (ledger.IsSettled(session, situations.Now) && !ledger.IsWinding(session))
        {
            return;
        }

        foreach (var job in integrationEvent.Job.Match<JobId[]>(found => [found], () => []))
        {
            await WatchAsync(job, session, cancellationToken);
        }
    }

    private async Task WatchAsync(JobId job, SessionId session, CancellationToken cancellationToken)
    {
        var counting = usage.OfSession(session).Match<IReadOnlyList<UsageLimit>>(
            summary => Capacity.Counting(summary.Limits, situations.Now),
            () => []);
        var used = counting.Select(limit => limit.UsedFraction).DefaultIfEmpty(0).Max();
        var read = used > 0 ? await situations.RulesAsync(job, cancellationToken) : Option<LimitRules>.None;

        if (read.Match(found => used < found.Threshold, () => true))
        {
            return;
        }

        var known = read.Match(found => found, () => LimitRules.Default);

        var reading = new Reading(used, Capacity.Of(counting, known.Threshold, situations.Now).At);

        if (ledger.IsWinding(session))
        {
            await CapAtAsync(job, session, reading, known, cancellationToken);

            return;
        }

        if (used < known.HardCap && looked.TryGetValue(session, out var last) && situations.Now - last < Patience)
        {
            return;
        }

        looked[session] = situations.Now;
        await (await situations.OfAsync(job, cancellationToken)).Match(
            situation => DecideAsync(situation, session, reading, cancellationToken),
            () => Task.CompletedTask);
    }

    private async Task DecideAsync(JobSituation situation, SessionId session, Reading reading, CancellationToken cancellationToken)
    {
        if (situation.History.Summary.Status != JobStatus.Running || !OnSession(situation, session))
        {
            return;
        }

        var decision = HandoffPolicy.ByThreshold(situation.Situation);

        if (decision.Verdict != Verdict.HandOff)
        {
            return;
        }

        var kept = situation.Situation.Rules;
        await decision.Why.Match(
            why => reading.Used >= kept.HardCap ? moves.CapAsync(situation, session, why, reading.Until, cancellationToken)
                : kept.WindDown ? moves.WindDownAsync(situation, session, why, reading.Until, cancellationToken)
                : RelayAtTurnEndAsync(session, reading),
            () => Task.CompletedTask);
    }

    private Task RelayAtTurnEndAsync(SessionId session, Reading reading)
    {
        moves.RelayAtTurnEnd(session, reading.Until);

        return Task.CompletedTask;
    }

    private async Task CapAtAsync(JobId job, SessionId session, Reading reading, LimitRules known, CancellationToken cancellationToken)
    {
        if (reading.Used < known.HardCap)
        {
            return;
        }

        await (await situations.OfAsync(job, cancellationToken)).Match(
            situation => situation.History.Summary.Status == JobStatus.Running && OnSession(situation, session)
                ? moves.CapAsync(situation, session, new LimitReason(situation.Connection, Window(situation), reading.Used, known.HardCap), reading.Until, cancellationToken)
                : Task.CompletedTask,
            () => Task.CompletedTask);
    }

    private static bool OnSession(JobSituation situation, SessionId session) =>
        situation.History.Sessions is [.., var last] && last.Session == session;

    private static string Window(JobSituation situation) =>
        Capacity.Counting(situation.Situation.Own.Limits, situation.Situation.Now).OrderByDescending(limit => limit.UsedFraction).Select(limit => limit.Window).FirstOrDefault() ?? string.Empty;

    private readonly record struct Reading(double Used, Option<DateTimeOffset> Until);
}
