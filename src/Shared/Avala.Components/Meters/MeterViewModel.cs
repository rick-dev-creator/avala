using System.Globalization;
using Avala.Sdk;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Components.Meters;

[INotifyPropertyChanged]
public sealed partial class MeterViewModel : IMeterViewModel
{
    public const double AttentionMargin = 0.1;

    public MeterViewModel(string label, Option<double> threshold)
    {
        Label = label;
        HasThreshold = threshold.IsSome;
        Threshold = threshold.Match(value => Math.Clamp(value, 0, 1), () => 0d);
        Reading = Percent(0);
    }

    public string Label { get; }

    public bool HasThreshold { get; }

    public double Threshold { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNearThreshold))]
    public partial double Fraction { get; private set; }

    [ObservableProperty]
    public partial string Reading { get; private set; }

    public bool IsNearThreshold => HasThreshold && Fraction >= Threshold - AttentionMargin;

    public void Show(double fraction) => Show(fraction, Option<string>.None);

    public void Show(double fraction, Option<string> detail)
    {
        Fraction = Math.Clamp(fraction, 0, 1);
        Reading = detail.Match(text => $"{Percent(Fraction)} · {text}", () => Percent(Fraction));
    }

    private static string Percent(double fraction) =>
        (fraction * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
}
