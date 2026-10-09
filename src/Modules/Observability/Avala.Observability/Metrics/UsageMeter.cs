using System.Diagnostics;
using System.Diagnostics.Metrics;
using Avala.Agents.Contracts.Events;
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

    public void RecordUsage(UsageSource source, TokenUsage tokens, Option<Cost> cost)
    {
        Count(source, "input", tokens.Input);
        Count(source, "output", tokens.Output);
        Count(source, "cache_read", tokens.CacheRead);
        Count(source, "cache_write", tokens.CacheWrite);
        Count(source, "reasoning", tokens.Reasoning);

        foreach (var reported in cost.Match<Cost[]>(reported => [reported], () => []))
        {
            this.cost.Add((double)reported.Amount, Tags(source, ("avala.currency", reported.Currency)));
        }
    }

    public void RecordTurn(UsageSource source, TurnOutcome outcome, Option<TimeSpan> duration)
    {
        var tags = Tags(source, ("avala.turn.outcome", outcome.ToString()));
        turns.Add(1, tags);

        foreach (var elapsed in duration.Match<TimeSpan[]>(elapsed => [elapsed], () => []))
        {
            turnDuration.Record(elapsed.TotalSeconds, tags);
        }
    }

    public void RecordLimit(UsageSource source, UsageLimit limit) =>
        limitUsed.Record(limit.UsedFraction, Tags(source, ("avala.limit.window", limit.Window)));

    public void Dispose() => Meter.Dispose();

    private static TagList Tags(UsageSource source, (string Key, string Value) tag)
    {
        var tags = new TagList { { tag.Key, tag.Value } };

        foreach (var provider in source.Provider.Match<string[]>(known => [known.Id], () => []))
        {
            tags.Add("avala.provider", provider);
        }

        foreach (var connection in source.Connection.Match<string[]>(known => [known.Value], () => []))
        {
            tags.Add("avala.connection", connection);
        }

        return tags;
    }

    private void Count(UsageSource source, string type, long amount) =>
        tokens.Add(amount, Tags(source, ("avala.token.type", type)));
}
