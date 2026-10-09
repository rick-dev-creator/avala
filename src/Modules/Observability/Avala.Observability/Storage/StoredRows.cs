using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Observability.Usage;
using Avala.Sdk;

namespace Avala.Observability.Storage;

internal sealed class StoredSession
{
    public Guid Session { get; init; }

    public string ProviderId { get; init; } = string.Empty;

    public string ProviderName { get; init; } = string.Empty;

    public string AccountId { get; init; } = string.Empty;

    public string AccountLabel { get; init; } = string.Empty;

    public string Connection { get; init; } = string.Empty;

    public Guid Job { get; init; }

    public static StoredSession Of(SessionUsage usage) => new()
    {
        Session = usage.Session.Value,
        ProviderId = usage.Provider.Match(provider => provider.Id, () => string.Empty),
        ProviderName = usage.Provider.Match(provider => provider.Name, () => string.Empty),
        AccountId = usage.Account.Match(account => account.Id, () => string.Empty),
        AccountLabel = usage.Account.Match(account => account.Label, () => string.Empty),
        Connection = usage.Connection.Match(connection => connection.Value, () => string.Empty),
        Job = usage.Job.Match(job => job.Value, () => Guid.Empty),
    };

    public SessionUsage Usage() =>
        new SessionUsage(new SessionId(Session)).Attributed(
            ProviderId.Length == 0 ? Option<ProviderInfo>.None : new ProviderInfo(ProviderId, ProviderName),
            AccountId.Length == 0 ? Option<AgentAccount>.None : new AgentAccount(AccountId, AccountLabel),
            Connection.Length == 0 ? Option<ConnectionName>.None : new ConnectionName(Connection),
            Job == Guid.Empty ? Option<JobId>.None : new JobId(Job));
}

internal sealed class StoredFact
{
    public int Key { get; init; }

    public Guid Session { get; init; }

    public long At { get; init; }

    public string Kind { get; init; } = string.Empty;

    public long Input { get; init; }

    public long Output { get; init; }

    public long CacheRead { get; init; }

    public long CacheWrite { get; init; }

    public long Reasoning { get; init; }

    public decimal Amount { get; init; }

    public string Currency { get; init; } = string.Empty;

    public string Window { get; init; } = string.Empty;

    public double UsedFraction { get; init; }

    public long ResetsAt { get; init; } = -1;

    public string Outcome { get; init; } = string.Empty;

    public long Duration { get; init; }

    public static StoredFact Of(UsageFact fact) => new()
    {
        Session = fact.Session.Value,
        At = fact.At.UtcTicks,
        Kind = fact.Kind.ToString(),
        Input = fact.Tokens.Input,
        Output = fact.Tokens.Output,
        CacheRead = fact.Tokens.CacheRead,
        CacheWrite = fact.Tokens.CacheWrite,
        Reasoning = fact.Tokens.Reasoning,
        Amount = fact.Cost.Match(cost => cost.Amount, () => 0m),
        Currency = fact.Cost.Match(cost => cost.Currency, () => string.Empty),
        Window = fact.Limit.Match(limit => limit.Window, () => string.Empty),
        UsedFraction = fact.Limit.Match(limit => limit.UsedFraction, () => 0d),
        ResetsAt = fact.Limit.Bind(limit => limit.ResetsAt).Match(at => at.UtcTicks, () => -1L),
        Outcome = fact.Outcome.Match(outcome => outcome.ToString(), () => string.Empty),
        Duration = fact.Duration.Ticks,
    };

    public UsageFact Fact() => new(
        new SessionId(Session),
        Moment(At),
        Enum.Parse<FactKind>(Kind),
        new TokenUsage(Input, Output, CacheRead, CacheWrite, Reasoning),
        Currency.Length == 0 ? Option<Cost>.None : new Cost(Amount, Currency),
        Window.Length == 0 ? Option<UsageLimit>.None : new UsageLimit(Window, UsedFraction, ResetsAt < 0 ? Option<DateTimeOffset>.None : Moment(ResetsAt)),
        Outcome.Length == 0 ? Option<TurnOutcome>.None : Enum.Parse<TurnOutcome>(Outcome),
        TimeSpan.FromTicks(Duration));

    private static DateTimeOffset Moment(long ticks) => new(ticks, TimeSpan.Zero);
}
