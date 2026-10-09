using System.Collections.Immutable;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;

namespace Avala.Observability.Usage;

internal sealed record SessionUsage(SessionId Session)
{
    public Option<ProviderInfo> Provider { get; private init; }

    public Option<AgentAccount> Account { get; private init; }

    public Option<ConnectionName> Connection { get; private init; }

    public Option<JobId> Job { get; private init; }

    public DateTimeOffset Opened { get; private init; }

    public TokenUsage Tokens { get; private init; }

    public ImmutableDictionary<string, decimal> Costs { get; private init; } = ImmutableDictionary<string, decimal>.Empty;

    public int UnpricedReports { get; private init; }

    public TurnTally Turns { get; private init; }

    public ImmutableDictionary<string, LimitReading> Limits { get; private init; } = ImmutableDictionary<string, LimitReading>.Empty;

    private ImmutableDictionary<TurnId, DateTimeOffset> OpenTurns { get; init; } = ImmutableDictionary<TurnId, DateTimeOffset>.Empty;

    private ImmutableHashSet<TurnId> EndedTurns { get; init; } = [];

    public SessionUsage OpenedBy(ProviderInfo provider, Option<AgentAccount> account, ConnectionName connection, DateTimeOffset at) =>
        this with { Provider = provider, Account = account, Connection = connection, Opened = at };

    public SessionUsage WorkingOn(JobId job) => this with { Job = job };

    public SessionUsage Attributed(Option<ProviderInfo> provider, Option<AgentAccount> account, Option<ConnectionName> connection, Option<JobId> job) =>
        this with { Provider = provider, Account = account, Connection = connection, Job = job };

    public SessionUsage OpenedAt(DateTimeOffset at) => this with { Opened = at };

    public Option<UsageSession> Seen =>
        Provider.Bind(provider => Connection.Map(connection => new UsageSession(Session, provider, Account, connection, Opened) { Job = Job }));

    public SessionUsage Recorded(UsageFact fact) => fact.Kind switch
    {
        FactKind.Usage => Bill(fact.Tokens, fact.Cost),
        FactKind.Limit => fact.Limit.Match(limit => this with { Limits = Limits.SetItem(limit.Window, new LimitReading(limit, fact.At)) }, () => this),
        _ => fact.Outcome.Match(outcome => this with { Turns = Turns.Counting(outcome, fact.Duration) }, () => this),
    };

    public Option<TimeSpan> Elapsed(TurnId turn, DateTimeOffset at) =>
        OpenTurns.TryGetValue(turn, out var started) ? at - started : Option<TimeSpan>.None;

    public SessionUsage Apply(IAgentEvent agentEvent, DateTimeOffset at) => agentEvent switch
    {
        TurnStarted started => Begin(started.Turn, at),
        UsageReported usage => Bill(usage.Tokens, usage.Cost),
        LimitReported limit => this with { Limits = Limits.SetItem(limit.Limit.Window, new LimitReading(limit.Limit, at)) },
        TurnCompleted completed => End(completed, at),
        _ => this,
    };

    private SessionUsage Begin(TurnId turn, DateTimeOffset at) =>
        OpenTurns.ContainsKey(turn) || EndedTurns.Contains(turn) ? this : this with { OpenTurns = OpenTurns.Add(turn, at) };

    private SessionUsage Bill(TokenUsage tokens, Option<Cost> priced) => priced.Match(
        cost => this with
        {
            Tokens = Tokens + tokens,
            Costs = Costs.SetItem(cost.Currency, Costs.GetValueOrDefault(cost.Currency) + cost.Amount),
        },
        () => this with { Tokens = Tokens + tokens, UnpricedReports = UnpricedReports + 1 });

    private SessionUsage End(TurnCompleted completed, DateTimeOffset at) =>
        EndedTurns.Contains(completed.Turn)
            ? this
            : this with
            {
                Turns = Turns.Counting(completed.Outcome, Elapsed(completed.Turn, at).Match(elapsed => elapsed, () => TimeSpan.Zero)),
                OpenTurns = OpenTurns.Remove(completed.Turn),
                EndedTurns = EndedTurns.Add(completed.Turn),
            };
}
