using System.Collections.Immutable;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Permissions.Policies;
using Avala.Sdk;

namespace Avala.Permissions.Governance;

internal sealed class GovernanceBook(IGovernanceStore store) : IPermissionAudit, IStartupTask
{
    private ImmutableDictionary<SessionId, GovernedSession> sessions = ImmutableDictionary<SessionId, GovernedSession>.Empty;
    private ImmutableDictionary<JobId, ImmutableList<PolicyRule>> rules = ImmutableDictionary<JobId, ImmutableList<PolicyRule>>.Empty;
    private ImmutableList<HumanAnswer> earlierAnswers = [];
    private ImmutableList<HumanAnswer> answers = [];

    public GovernedSession Of(SessionId session) => Volatile.Read(ref sessions).GetValueOrDefault(session) ?? new GovernedSession(session);

    public void Keep(GovernedSession session) =>
        ImmutableInterlocked.AddOrUpdate(ref sessions, session.Session, session, (_, _) => session);

    public async Task OpenedAsync(GovernedSession session, SessionPolicy report, CancellationToken cancellationToken)
    {
        Keep(session);
        await store.RecordAsync(report, cancellationToken);
    }

    public async Task WorkingOnAsync(GovernedSession session, CancellationToken cancellationToken)
    {
        Keep(session);
        await session.Autonomy.Match(autonomy => store.RecordAsync(autonomy, cancellationToken), () => Task.CompletedTask);
    }

    public async Task DecidedAsync(PolicyDecision decision, CancellationToken cancellationToken)
    {
        Keep(Of(decision.Session).Decided(decision));
        await store.RecordAsync(decision, cancellationToken);
    }

    public async Task AskedAsync(FormDecision decision, CancellationToken cancellationToken)
    {
        Keep(Of(decision.Session).Asked(decision));
        await store.RecordAsync(decision, cancellationToken);
    }

    public async Task WithdrawnAsync(PolicyDecision decision, CancellationToken cancellationToken)
    {
        Keep(Of(decision.Session).Withdrawn(decision));
        await store.RecordAsync(decision, cancellationToken);
    }

    public async Task WithdrawnAsync(FormDecision decision, CancellationToken cancellationToken)
    {
        Keep(Of(decision.Session).Withdrawn(decision));
        await store.RecordAsync(decision, cancellationToken);
    }

    public async Task RecordAsync(HumanAnswer answer, CancellationToken cancellationToken)
    {
        ImmutableInterlocked.Update(ref answers, recorded => recorded.Add(answer));
        await store.RecordAsync(answer, cancellationToken);
    }

    public void Remember(JobId job, PolicyRule rule) =>
        ImmutableInterlocked.AddOrUpdate(ref rules, job, [rule], (_, kept) => kept.Add(rule));

    public void Forget(JobId job, PolicyRule rule) =>
        ImmutableInterlocked.AddOrUpdate(ref rules, job, [], (_, kept) => kept.Remove(rule));

    public async Task EndedAsync(EndedJob ended, CancellationToken cancellationToken)
    {
        ImmutableInterlocked.TryRemove(ref rules, ended.Job, out _);
        await store.RecordAsync(ended, cancellationToken);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var history = await store.EarlierRunsAsync(cancellationToken);

        foreach (var restored in Restored(history))
        {
            ImmutableInterlocked.TryAdd(ref sessions, restored.Session, restored);
        }

        foreach (var (job, kept) in JobRules(history))
        {
            ImmutableInterlocked.TryAdd(ref rules, job, kept);
        }

        Volatile.Write(ref earlierAnswers, [.. history.Answers]);
    }

    public Option<SessionPolicy> PolicyOf(SessionId session) => Of(session).Report;

    public Option<SessionAutonomy> AutonomyOf(SessionId session) => Of(session).Autonomy;

    public IReadOnlyList<PolicyRule> JobRulesOf(JobId job) => Volatile.Read(ref rules).GetValueOrDefault(job) ?? [];

    public IReadOnlyList<PolicyRule> RulesOf(GovernedSession session) => session.Job.Match(JobRulesOf, () => []);

    public IReadOnlyList<PolicyDecision> OfSession(SessionId session) => Of(session).Decisions;

    public IReadOnlyList<PolicyDecision> OfJob(JobId job) =>
        [.. OfEverySession(job).SelectMany(session => session.Decisions).OrderBy(decision => decision.At)];

    public IReadOnlyList<FormDecision> FormsOfSession(SessionId session) => Of(session).Forms;

    public IReadOnlyList<FormDecision> FormsOfJob(JobId job) =>
        [.. OfEverySession(job).SelectMany(session => session.Forms).OrderBy(decision => decision.At)];

    public IReadOnlyList<HumanAnswer> AnswersOfJob(JobId job) =>
        [.. Volatile.Read(ref earlierAnswers).Concat(Volatile.Read(ref answers)).Where(answer => answer.Job == Option<JobId>.Some(job))];

    private static IEnumerable<GovernedSession> Restored(GovernanceHistory history) =>
        history.Policies.Select(policy => policy.Session)
            .Concat(history.Autonomies.Select(autonomy => autonomy.Session))
            .Concat(history.Decisions.Select(decision => decision.Session))
            .Concat(history.Forms.Select(form => form.Session))
            .Distinct()
            .Select(session => Restored(history, session));

    private static GovernedSession Restored(GovernanceHistory history, SessionId session)
    {
        var autonomy = history.Autonomies.LastOrDefault(found => found.Session == session).ToOption();
        var decisions = Latest(history.Decisions.Where(decision => decision.Session == session), decision => decision.Item);
        var forms = Latest(history.Forms.Where(form => form.Session == session), form => form.Item);

        return new GovernedSession(session) with
        {
            Report = history.Policies.LastOrDefault(policy => policy.Session == session).ToOption(),
            Job = autonomy.Map(found => found.Job).Match(
                Option<JobId>.Some,
                () => decisions.Select(decision => decision.Job).Concat(forms.Select(form => form.Job)).FirstOrDefault(job => job.IsSome)),
            Autonomy = autonomy,
            Decisions = decisions,
            Forms = forms,
        };
    }

    private static IEnumerable<(JobId Job, ImmutableList<PolicyRule> Rules)> JobRules(GovernanceHistory history) =>
        history.Answers
            .SelectMany(answer => answer.Job.Bind(job => answer.Rule.Map(rule => (Job: job, Rule: rule))).Match(found => new[] { found }, () => []))
            .Where(kept => kept.Rule.Origin == RuleOrigin.Job && !history.Ended.Contains(kept.Job))
            .GroupBy(kept => kept.Job)
            .Select(group => (group.Key, group.Select(kept => kept.Rule).ToImmutableList()));

    private static List<T> Latest<T>(IEnumerable<T> recorded, Func<T, ItemId> item) =>
        [.. recorded.GroupBy(item).Select(kept => kept.Last())];

    private IEnumerable<GovernedSession> OfEverySession(JobId job) =>
        Volatile.Read(ref sessions).Values.Where(session => session.Job == Option<JobId>.Some(job));
}
