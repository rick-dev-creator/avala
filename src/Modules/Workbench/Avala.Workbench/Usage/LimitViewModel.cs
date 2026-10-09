using Avala.Agents.Contracts.Events;
using Avala.Components.Meters;
using Avala.Sdk;
using Avala.Workbench.Presenting;

namespace Avala.Workbench.Usage;

internal interface ILimitViewModel
{
    string Window { get; }

    string Label { get; }

    double Used { get; }

    string UsedText { get; }

    string Resets { get; }

    string HoldAt { get; }

    bool HasHold { get; }

    double Hold { get; }

    bool IsNear { get; }

    bool ReachesHold { get; }
}

internal sealed class LimitViewModel(UsageLimit limit, Option<double> holdAt) : ILimitViewModel
{
    public string Window { get; } = limit.Window;

    public string Label { get; } = UsagePhrases.Window(limit.Window);

    public double Used { get; } = Math.Clamp(limit.UsedFraction, 0, 1);

    public string UsedText { get; } = Amounts.Percent(limit.UsedFraction);

    public string Resets { get; } = limit.ResetsAt.Match(at => $"resets {Amounts.Time(at)}", () => "no reset reported");

    public string HoldAt { get; } = holdAt.Match(threshold => $"Jobs on this connection hold at the {Amounts.Percent(threshold)} threshold.", () => string.Empty);

    public bool HasHold { get; } = holdAt.IsSome;

    public double Hold { get; } = holdAt.Match(threshold => Math.Clamp(threshold, 0, 1), () => 0d);

    public bool IsNear { get; } = holdAt.Match(threshold => limit.UsedFraction >= threshold - MeterViewModel.AttentionMargin, () => false);

    public bool ReachesHold { get; } = holdAt.Match(threshold => limit.UsedFraction >= threshold, () => false);
}

internal static class UsagePhrases
{
    public static string Window(string window) =>
        window.Length == 0 ? "Usage window"
        : window.Length > 1 && char.IsDigit(window[0]) && window[^1] is 'h' or 'd' && int.TryParse(window[..^1], System.Globalization.CultureInfo.InvariantCulture, out var count)
            ? $"{count}-{(window[^1] == 'h' ? "hour" : "day")} window"
            : $"{char.ToUpperInvariant(window[0])}{window[1..]} window";
}
