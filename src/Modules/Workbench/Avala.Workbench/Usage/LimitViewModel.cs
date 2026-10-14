using Avala.Components.Meters;
using Avala.Sdk;
using Avala.Workbench.Presenting;
using Avala.Workbench.Spending;
using CommunityToolkit.Mvvm.ComponentModel;

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

    string Outside { get; }
}

[INotifyPropertyChanged]
internal sealed partial class LimitViewModel : ILimitViewModel
{
    public LimitViewModel(LimitReading reading, Option<double> holdAt)
    {
        Window = reading.Limit.Window;
        Label = UsagePhrases.Window(reading.Limit.Window);
        UsedText = string.Empty;
        Resets = string.Empty;
        HoldAt = string.Empty;
        Outside = string.Empty;
        Show(reading, holdAt);
    }

    public string Window { get; }

    public string Label { get; }

    [ObservableProperty]
    public partial double Used { get; private set; }

    [ObservableProperty]
    public partial string UsedText { get; private set; }

    [ObservableProperty]
    public partial string Resets { get; private set; }

    [ObservableProperty]
    public partial string HoldAt { get; private set; }

    [ObservableProperty]
    public partial bool HasHold { get; private set; }

    [ObservableProperty]
    public partial double Hold { get; private set; }

    [ObservableProperty]
    public partial bool IsNear { get; private set; }

    [ObservableProperty]
    public partial bool ReachesHold { get; private set; }

    [ObservableProperty]
    public partial bool IsExpired { get; private set; }

    [ObservableProperty]
    public partial string Outside { get; private set; }

    public void Show(LimitReading reading, Option<double> holdAt)
    {
        Used = Math.Clamp(reading.Used, 0, 1);
        UsedText = reading.Expired ? "reset" : Amounts.Percent(reading.Limit.UsedFraction);
        Resets = reading.Limit.ResetsAt.Match(
            at => reading.Expired ? $"reset {Amounts.Time(at)}, no reading since" : $"resets {Amounts.Time(at)}",
            () => "no reset reported");
        HoldAt = holdAt.Match(threshold => $"Jobs on this connection hold at the {Amounts.Percent(threshold)} threshold.", () => string.Empty);
        HasHold = holdAt.IsSome;
        Hold = holdAt.Match(threshold => Math.Clamp(threshold, 0, 1), () => 0d);
        IsNear = holdAt.Match(threshold => reading.Used >= threshold - MeterViewModel.AttentionMargin, () => false);
        ReachesHold = holdAt.Match(threshold => reading.Used >= threshold, () => false);
        IsExpired = reading.Expired;
        Outside = UsagePhrases.Outside(reading.Expired ? 0 : reading.Outside);
    }
}

internal static class UsagePhrases
{
    public static string Shared(string connection) => $"Shared with every app signed in to {connection}, not only Avala.";

    public static string Outside(double outside) =>
        outside < Avala.Workbench.Spending.WindowWatch.Moved ? string.Empty : $"Rose {Amounts.Percent(outside)} while Avala was idle, from use outside Avala.";

    public static string Window(string window) =>
        window.Length == 0 ? "Usage window"
        : window.Length > 1 && char.IsDigit(window[0]) && window[^1] is 'h' or 'd' && int.TryParse(window[..^1], System.Globalization.CultureInfo.InvariantCulture, out var count)
            ? $"{count}-{(window[^1] == 'h' ? "hour" : "day")} window"
            : $"{char.ToUpperInvariant(window[0])}{window[1..]} window";

    public static string Held(int interventions) => interventions switch
    {
        0 => string.Empty,
        1 => "held once",
        _ => $"held {interventions} times",
    };
}
