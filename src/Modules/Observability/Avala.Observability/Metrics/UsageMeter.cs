using System.Diagnostics;
using System.Diagnostics.Metrics;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Observability.Tracking;
using Avala.Sdk;

namespace Avala.Observability.Metrics;

internal sealed class UsageMeter : IUsageMetrics, IDisposable
{
    public const string Name = "Avala.Observability";

    private readonly Counter<long> tokens;
    private readonly Counter<double> cost;
    private readonly Counter<long> turns;
    private readonly Histogram<double> turnDuration;
    private readonly Gauge<double> limitUsed;

    public UsageMeter()
    {
        Meter = new Meter(Name);
        tokens = Meter.CreateCounter<long>("avala.agent.tokens", "{token}", "Tokens used by agents");
        cost = Meter.CreateCounter<double>("avala.agent.cost", "{currency}", "Cost reported by agents");
        turns = Meter.CreateCounter<long>("avala.agent.turns", "{turn}", "Agent turns by outcome");
        turnDuration = Meter.CreateHistogram<double>("avala.agent.turn.duration", "s", "Duration of agent turns");
        limitUsed = Meter.CreateGauge<double>("avala.agent.limit.used", "1", "Fraction of a usage limit used");
    }

    public Meter Meter { get; }

    public void RecordUsage(Option<ProviderInfo> provider, TokenUsage tokens, Option<Cost> cost)
    {
        Count(provider, "input", tokens.Input);
        Count(provider, "output", tokens.Output);
        Count(provider, "cache_read", tokens.CacheRead);
        Count(provider, "cache_write", tokens.CacheWrite);
        Count(provider, "reasoning", tokens.Reasoning);

        foreach (var reported in cost.Match<Cost[]>(reported => [reported], () => []))
        {
            this.cost.Add((double)reported.Amount, Tags(provider, ("avala.currency", reported.Currency)));
        }
    }

    public void RecordTurn(Option<ProviderInfo> provider, TurnOutcome outcome, Option<TimeSpan> duration)
    {
        var tags = Tags(provider, ("avala.turn.outcome", outcome.ToString()));
        turns.Add(1, tags);

        foreach (var elapsed in duration.Match<TimeSpan[]>(elapsed => [elapsed], () => []))
        {
            turnDuration.Record(elapsed.TotalSeconds, tags);
        }
    }

    public void RecordLimit(Option<ProviderInfo> provider, UsageLimit limit) =>
        limitUsed.Record(limit.UsedFraction, Tags(provider, ("avala.limit.window", limit.Window)));

    public void Dispose() => Meter.Dispose();

    private static TagList Tags(Option<ProviderInfo> provider, (string Key, string Value) tag)
    {
        var tags = new TagList { { tag.Key, tag.Value } };

        foreach (var known in provider.Match<ProviderInfo[]>(known => [known], () => []))
        {
            tags.Add("avala.provider", known.Id);
        }

        return tags;
    }

    private void Count(Option<ProviderInfo> provider, string type, long amount) =>
        tokens.Add(amount, Tags(provider, ("avala.token.type", type)));
}
