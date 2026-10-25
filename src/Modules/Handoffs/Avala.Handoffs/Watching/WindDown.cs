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

    private readonly Dictionary<SessionId, LimitRules> rules = [];
    private readonly Dictionary<SessionId, DateTimeOffset> looked = [];

    public async ValueTask HandleAsync(UsageRecorded integrationEvent, CancellationToken cancellationToken)
    {
        var session = integrationEvent.Session;

        if (ledger.IsSettled(session) && !ledger.IsWinding(session))
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
        var used = usage.OfSession(session).Match(
            summary => Capacity.Counting(summary.Limits, situations.Now).Select(limit => limit.UsedFraction).DefaultIfEmpty(0).Max(),
            () => 0);

        var read = used > 0 ? await RulesOfAsync(job, session, cancellationToken) : Option<LimitRules>.None;

        if (read.Match(found => used < found.Threshold, () => true))
        {
            return;
        }

        var known = read.Match(found => found, () => LimitRules.Default);

        if (ledger.IsWinding(session))
        {
            await CapAtAsync(job, session, used, known, cancellationToken);

            return;
        }

        if (looked.TryGetValue(session, out var last) && situations.Now - last < Patience)
        {
            return;
        }

        looked[session] = situations.Now;
        await (await situations.OfAsync(job, cancellationToken)).Match(
            situation => DecideAsync(situation, session, used, cancellationToken),
            () => Task.CompletedTask);
    }

    private async Task DecideAsync(JobSituation situation, SessionId session, double used, CancellationToken cancellationToken)
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
            why => kept.WindDown && used < kept.HardCap
                ? moves.WindDownAsync(situation, session, why, cancellationToken)
                : moves.CapAsync(situation, session, why, cancellationToken),
            () => Task.CompletedTask);
    }

    private async Task CapAtAsync(JobId job, SessionId session, double used, LimitRules known, CancellationToken cancellationToken)
    {
        if (used < known.HardCap)
        {
            return;
        }

        await (await situations.OfAsync(job, cancellationToken)).Match(
            situation => situation.History.Summary.Status == JobStatus.Running && OnSession(situation, session)
                ? moves.CapAsync(situation, session, new LimitReason(situation.Connection, Window(situation), used, known.HardCap), cancellationToken)
                : Task.CompletedTask,
            () => Task.CompletedTask);
    }

    private static bool OnSession(JobSituation situation, SessionId session) =>
        situation.History.Sessions is [.., var last] && last.Session == session;

    private static string Window(JobSituation situation) =>
        Capacity.Counting(situation.Situation.Own.Limits, situation.Situation.Now).OrderByDescending(limit => limit.UsedFraction).Select(limit => limit.Window).FirstOrDefault() ?? string.Empty;

    private async Task<Option<LimitRules>> RulesOfAsync(JobId job, SessionId session, CancellationToken cancellationToken)
    {
        if (rules.TryGetValue(session, out var known))
        {
            return known;
        }

        var read = await situations.RulesAsync(job, cancellationToken);

        foreach (var found in read.Match<LimitRules[]>(found => [found], () => []))
        {
            rules[session] = found;
        }

        return read;
    }
}
