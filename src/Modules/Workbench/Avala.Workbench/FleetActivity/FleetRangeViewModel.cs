using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.FleetActivity;

internal interface IFleetRangeViewModel
{
    IReadOnlyList<IFleetRangeOptionViewModel> Options { get; }

    string Caption { get; }

    bool IsCustomOpen { get; }

    string CustomFrom { get; set; }

    string CustomTo { get; set; }

    string Refusal { get; }

    bool HasRefusal { get; }

    IRelayCommand ApplyCustomCommand { get; }
}

internal interface IFleetRangeOptionViewModel
{
    string Label { get; }

    bool IsSelected { get; }

    IRelayCommand ChooseCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class FleetRangeViewModel : IFleetRangeViewModel
{
    private const string DateFormat = "yyyy-MM-dd";

    private readonly Func<DateOnly> today;

    private readonly List<FleetRangeOptionViewModel> options;

    public FleetRangeViewModel(Func<DateOnly> today)
    {
        this.today = today;
        options =
        [
            new(FleetRangeChoice.LastSevenDays, "7 days", Choose),
            new(FleetRangeChoice.LastThirtyDays, "30 days", Choose),
            new(FleetRangeChoice.ThisMonth, "This month", Choose),
            new(FleetRangeChoice.ThreeMonths, "3 months", Choose),
            new(FleetRangeChoice.SixMonths, "6 months", Choose),
            new(FleetRangeChoice.TwelveMonths, "12 months", Choose),
            new(FleetRangeChoice.Custom, "Custom", Choose),
        ];
        Current = FleetRanges.Of(FleetRangeChoice.LastSevenDays, today());
        Select(FleetRangeChoice.LastSevenDays);
    }

    public event EventHandler? Chosen;

    public IReadOnlyList<IFleetRangeOptionViewModel> Options => options;

    public FleetRange Current { get; private set; }

    [ObservableProperty]
    public partial string Caption { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsCustomOpen { get; private set; }

    [ObservableProperty]
    public partial string CustomFrom { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CustomTo { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Refusal { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasRefusal { get; private set; }

    public void Refresh()
    {
        if (Current.Choice != FleetRangeChoice.Custom)
        {
            Current = FleetRanges.Of(Current.Choice, today());
            Caption = Current.Caption;
        }
    }

    private void Choose(FleetRangeChoice choice)
    {
        if (choice == FleetRangeChoice.Custom)
        {
            CustomFrom = Current.From.ToString(DateFormat, CultureInfo.InvariantCulture);
            CustomTo = Current.To.AddDays(-1).ToString(DateFormat, CultureInfo.InvariantCulture);
            IsCustomOpen = true;

            return;
        }

        Show(FleetRanges.Of(choice, today()));
    }

    [RelayCommand]
    private void ApplyCustom()
    {
        if (!Date(CustomFrom, out var from) || !Date(CustomTo, out var to))
        {
            Refuse("Write the dates as 2026-10-01");

            return;
        }

        FleetRanges.Custom(from, to, today()).Match(
            range =>
            {
                Show(range);

                return true;
            },
            refusal =>
            {
                Refuse(FleetRanges.Said(refusal));

                return false;
            });
    }

    private static bool Date(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private void Show(FleetRange range)
    {
        Current = range;
        IsCustomOpen = false;
        Refusal = string.Empty;
        HasRefusal = false;
        Select(range.Choice);
        Chosen?.Invoke(this, EventArgs.Empty);
    }

    private void Refuse(string reason)
    {
        Refusal = reason;
        HasRefusal = true;
    }

    private void Select(FleetRangeChoice choice)
    {
        Caption = Current.Caption;

        foreach (var option in options)
        {
            option.IsSelected = option.Choice == choice;
        }
    }
}

[INotifyPropertyChanged]
internal sealed partial class FleetRangeOptionViewModel(FleetRangeChoice choice, string label, Action<FleetRangeChoice> choose) : IFleetRangeOptionViewModel
{
    public FleetRangeChoice Choice => choice;

    public string Label => label;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Choose() => choose(choice);
}
