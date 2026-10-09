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

    public TokenUsage Tokens { get; private init; }

    public ImmutableDictionary<string, decimal> Costs { get; private init; } = ImmutableDictionary<string, decimal>.Empty;

    public int UnpricedReports { get; private init; }

    public TurnTally Turns { get; private init; }

    public ImmutableDictionary<string, LimitReading> Limits { get; private init; } = ImmutableDictionary<string, LimitReading>.Empty;

    private ImmutableDictionary<TurnId, DateTimeOffset> OpenTurns { get; init; } = ImmutableDictionary<TurnId, DateTimeOffset>.Empty;

    private ImmutableHashSet<TurnId> EndedTurns { get; init; } = [];

    public SessionUsage OpenedBy(ProviderInfo provider, Option<AgentAccount> account, ConnectionName connection) =>
        this with { Provider = provider, Account = account, Connection = connection };

    public SessionUsage WorkingOn(JobId job) => this with { Job = job };

    public Option<TimeSpan> Elapsed(TurnId turn, DateTimeOffset at) =>
        OpenTurns.TryGetValue(turn, out var started) ? at - started : Option<TimeSpan>.None;

    public SessionUsage Apply(IAgentEvent agentEvent, DateTimeOffset at) => agentEvent switch
    {
        TurnStarted started => Begin(started.Turn, at),
        UsageReported usage => Bill(usage),
        LimitReported limit => this with { Limits = Limits.SetItem(limit.Limit.Window, new LimitReading(limit.Limit, at)) },
        TurnCompleted completed => End(completed, at),
        _ => this,
    };

    private SessionUsage Begin(TurnId turn, DateTimeOffset at) =>
        OpenTurns.ContainsKey(turn) || EndedTurns.Contains(turn) ? this : this with { OpenTurns = OpenTurns.Add(turn, at) };

    private SessionUsage Bill(UsageReported usage) => usage.Cost.Match(
        cost => this with
        {
            Tokens = Tokens + usage.Tokens,
            Costs = Costs.SetItem(cost.Currency, Costs.GetValueOrDefault(cost.Currency) + cost.Amount),
        },
        () => this with { Tokens = Tokens + usage.Tokens, UnpricedReports = UnpricedReports + 1 });

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
