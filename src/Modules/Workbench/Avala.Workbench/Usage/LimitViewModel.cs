using Avala.Components.Meters;
using Avala.Sdk;
using Avala.Workbench.Presenting;
using Avala.Workbench.Spending;

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

    bool IsExpired { get; }
}

internal sealed class LimitViewModel(LimitReading reading, Option<double> holdAt) : ILimitViewModel
{
    public string Window { get; } = reading.Limit.Window;

    public string Label { get; } = UsagePhrases.Window(reading.Limit.Window);

    public double Used { get; } = Math.Clamp(reading.Used, 0, 1);

    public string UsedText { get; } = reading.Expired ? "reset" : Amounts.Percent(reading.Limit.UsedFraction);

    public string Resets { get; } = reading.Limit.ResetsAt.Match(
        at => reading.Expired ? $"reset {Amounts.Time(at)}, no reading since" : $"resets {Amounts.Time(at)}",
        () => "no reset reported");

    public string HoldAt { get; } = holdAt.Match(threshold => $"Jobs on this connection hold at the {Amounts.Percent(threshold)} threshold.", () => string.Empty);

    public bool HasHold { get; } = holdAt.IsSome;

    public double Hold { get; } = holdAt.Match(threshold => Math.Clamp(threshold, 0, 1), () => 0d);

    public bool IsNear { get; } = holdAt.Match(threshold => reading.Used >= threshold - MeterViewModel.AttentionMargin, () => false);

    public bool ReachesHold { get; } = holdAt.Match(threshold => reading.Used >= threshold, () => false);

    public bool IsExpired { get; } = reading.Expired;
}

internal static class UsagePhrases
{
    public static string Window(string window) =>
        window.Length == 0 ? "Usage window"
        : window.Length > 1 && char.IsDigit(window[0]) && window[^1] is 'h' or 'd' && int.TryParse(window[..^1], System.Globalization.CultureInfo.InvariantCulture, out var count)
            ? $"{count}-{(window[^1] == 'h' ? "hour" : "day")} window"
            : $"{char.ToUpperInvariant(window[0])}{window[1..]} window";
}
