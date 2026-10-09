using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Conversation;

internal sealed class TurnEndViewModel(TurnEndEntry entry) : ITimelineItem
{
    public TurnOutcome Outcome { get; } = entry.Outcome;

    public TimeSpan Duration { get; } = entry.Duration;

    public string Summary { get; } = ConversationPhrases.Turn(entry.Outcome, entry.Duration);

    public long Tokens { get; } = entry.Tokens.Input + entry.Tokens.Output + entry.Tokens.CacheRead + entry.Tokens.CacheWrite + entry.Tokens.Reasoning;

    public string Cost { get; } = string.Join(" + ", entry.Costs.Select(cost => string.Create(CultureInfo.InvariantCulture, $"{cost.Amount:0.####} {cost.Currency}")));

    public bool IsShown => true;

    public void Update(ITimelineEntry entry)
    {
    }
}
