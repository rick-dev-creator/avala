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
    private ImmutableDictionary<JobId, DateTimeOffset> raised = ImmutableDictionary<JobId, DateTimeOffset>.Empty;
    private ImmutableDictionary<SessionId, TaskCompletionSource> unassigned = ImmutableDictionary<SessionId, TaskCompletionSource>.Empty;

    public GovernedSession Of(SessionId session) => Volatile.Read(ref sessions).GetValueOrDefault(session) ?? new GovernedSession(session);

    public void Keep(GovernedSession session)
    {
        ImmutableInterlocked.AddOrUpdate(ref sessions, session.Session, session, (_, _) => session);

        if (session.Job.IsSome && ImmutableInterlocked.TryRemove(ref unassigned, session.Session, out var waiting))
        {
            waiting.TrySetResult();
        }
    }

    public async Task<GovernedSession> WorkingAsync(SessionId session, TimeSpan patience, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (Of(session).Job.IsSome)
        {
            return Of(session);
        }

        var waiting = ImmutableInterlocked.GetOrAdd(ref unassigned, session, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        try
        {
            if (Of(session).Job.IsNone)
            {
                await waiting.Task.WaitAsync(patience, clock, cancellationToken);
            }
        }
        catch (TimeoutException)
        {
            ImmutableInterlocked.TryRemove(ref unassigned, session, out _);
        }

        return Of(session);
    }

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
        Change(decision.Session, kept => kept.Decided(decision));
        await store.RecordAsync(decision, cancellationToken);
    }

    public async Task AskedAsync(FormDecision decision, CancellationToken cancellationToken)
    {
        Change(decision.Session, kept => decision.Delivery == DecisionDelivery.LeftToParent ? kept.Asked(decision).Escalating(decision.Item) : kept.Asked(decision));
        await store.RecordAsync(decision, cancellationToken);
    }

    public Task WithdrawnAsync(PolicyDecision decision, CancellationToken cancellationToken) => ReplacedAsync(decision, cancellationToken);

    public Task WithdrawnAsync(FormDecision decision, CancellationToken cancellationToken) => ReplacedAsync(decision, cancellationToken);

    public async Task ReplacedAsync(PolicyDecision decision, CancellationToken cancellationToken)
    {
        Change(decision.Session, kept => kept.Replaced(decision).Settled(decision.Item));
        await store.RecordAsync(decision, cancellationToken);
    }

    public async Task ReplacedAsync(FormDecision decision, CancellationToken cancellationToken)
    {
        Change(decision.Session, kept => kept.Replaced(decision).Settled(decision.Item));
        await store.RecordAsync(decision, cancellationToken);
    }

    public void Settle(SessionId session, ItemId item) => Change(session, kept => kept.Settled(item));

    public void Reopen(SessionId session, ItemId item) => Change(session, kept => kept.Escalating(item));

    public bool Claim(SessionId session, ItemId item) =>
        Take(session, kept => kept.Escalated.Contains(item) ? Option<ItemId>.Some(item) : Option<ItemId>.None, (kept, _) => kept.Settled(item)).IsSome;

    public async Task RecordAsync(HumanAnswer answer, CancellationToken cancellationToken)
    {
        ImmutableInterlocked.Update(ref answers, recorded => recorded.Add(answer));
        await store.RecordAsync(answer, cancellationToken);
    }

    public void Remember(JobId job, PolicyRule rule) =>
        ImmutableInterlocked.AddOrUpdate(ref rules, job, [rule], (_, kept) => kept.Add(rule));

    public void Forget(JobId job, PolicyRule rule) =>
        ImmutableInterlocked.AddOrUpdate(ref rules, job, [], (_, kept) => kept.Remove(rule));

    public bool Raise(JobId job, DateTimeOffset at)
    {
        var added = ImmutableInterlocked.TryAdd(ref raised, job, at);
        var since = Volatile.Read(ref raised).GetValueOrDefault(job, at);

        foreach (var session in OfEverySession(job).Select(kept => kept.Session).ToList())
        {
            Change(session, kept => kept.Raised(since));
        }

        return added;
    }

    public void Lower(JobId job)
    {
        ImmutableInterlocked.TryRemove(ref raised, job, out _);

        foreach (var session in OfEverySession(job).Select(kept => kept.Session).ToList())
        {
            Change(session, kept => kept.Raised(Option<DateTimeOffset>.None));
        }
    }

    public Option<DateTimeOffset> RaisedAt(JobId job) =>
        Volatile.Read(ref raised).TryGetValue(job, out var at) ? at : Option<DateTimeOffset>.None;

    public async Task<IReadOnlyList<SessionAutonomy>> RaisedAsync(JobId job, CancellationToken cancellationToken)
    {
        var autonomies = OfEverySession(job).SelectMany(session => session.Autonomy.Match(found => new[] { found }, () => [])).ToList();

        foreach (var autonomy in autonomies)
        {
            await store.RecordAsync(autonomy, cancellationToken);
        }

        return autonomies;
    }

    public async Task EndedAsync(EndedJob ended, CancellationToken cancellationToken)
    {
        ImmutableInterlocked.TryRemove(ref rules, ended.Job, out _);
        ImmutableInterlocked.TryRemove(ref raised, ended.Job, out _);
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

        foreach (var autonomy in history.Autonomies.Where(found => !history.Ended.Contains(found.Job)))
        {
            foreach (var at in autonomy.Raised.Match(found => new[] { found }, () => []))
            {
                ImmutableInterlocked.TryAdd(ref raised, autonomy.Job, at);
            }
        }

        Volatile.Write(ref earlierAnswers, [.. history.Answers]);
    }

    public Option<SessionPolicy> PolicyOf(SessionId session) => Of(session).Report;

    public Option<SessionAutonomy> AutonomyOf(SessionId session) => Of(session).Autonomy;

    public IReadOnlyList<PolicyRule> JobRulesOf(JobId job) => Volatile.Read(ref rules).GetValueOrDefault(job) ?? [];

    public IReadOnlyList<PolicyRule> RulesOf(GovernedSession session) => session.Job.Match(JobRulesOf, () => []);

    public IReadOnlyList<PolicyDecision> OfSession(SessionId session) => Of(session).Decisions;

    public IReadOnlyList<GovernedSession> SessionsOf(JobId job) => [.. OfEverySession(job)];

    public IReadOnlyList<PolicyDecision> OfJob(JobId job) =>
        [.. OfEverySession(job).SelectMany(session => session.Decisions).OrderBy(decision => decision.At)];

    public IReadOnlyList<FormDecision> FormsOfSession(SessionId session) => Of(session).Forms;

    public IReadOnlyList<FormDecision> FormsOfJob(JobId job) =>
        [.. OfEverySession(job).SelectMany(session => session.Forms).OrderBy(decision => decision.At)];

    public IReadOnlyList<HumanAnswer> AnswersOfJob(JobId job) =>
        [.. Volatile.Read(ref earlierAnswers).Concat(Volatile.Read(ref answers)).Where(answer => answer.Job == Option<JobId>.Some(job))];

    public IReadOnlyList<HumanAnswer> AnswersGivenBy(JobId parent) =>
        [.. Volatile.Read(ref earlierAnswers).Concat(Volatile.Read(ref answers)).Where(answer => answer.Parent == Option<JobId>.Some(parent))];

    private void Change(SessionId session, Func<GovernedSession, GovernedSession> change) =>
        ImmutableInterlocked.AddOrUpdate(ref sessions, session, id => change(new GovernedSession(id)), (_, kept) => change(kept));

    private Option<T> Take<T>(SessionId session, Func<GovernedSession, Option<T>> find, Func<GovernedSession, T, GovernedSession> change)
        where T : notnull
    {
        var taken = Option<T>.None;
        ImmutableInterlocked.Update(ref sessions, current =>
        {
            var kept = current.GetValueOrDefault(session) ?? new GovernedSession(session);
            taken = find(kept);

            return taken.Match(found => current.SetItem(session, change(kept, found)), () => current);
        });

        return taken;
    }

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
