using Avala.Agents.Contracts.Events;
using Avala.Sdk;
using Avala.Workbench.Presenting;

namespace Avala.Workbench.Usage;

internal interface ILimitViewModel
{
    string Window { get; }

    double Used { get; }

    string UsedText { get; }

    string Resets { get; }

    string HoldAt { get; }

    bool ReachesHold { get; }
}

internal sealed class LimitViewModel(UsageLimit limit, Option<double> holdAt) : ILimitViewModel
{
    public string Window { get; } = limit.Window;

    public double Used { get; } = limit.UsedFraction;

    public string UsedText { get; } = $"{Amounts.Percent(limit.UsedFraction)} used";

    public string Resets { get; } = limit.ResetsAt.Match(at => $"resets {Amounts.Time(at)}", () => "no reset reported");

    public string HoldAt { get; } = holdAt.Match(threshold => $"jobs are held at {Amounts.Percent(threshold)}", () => string.Empty);

    public bool ReachesHold { get; } = holdAt.Match(threshold => limit.UsedFraction >= threshold, () => false);
}
