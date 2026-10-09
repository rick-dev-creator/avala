using System.Collections.ObjectModel;
using System.Globalization;
using Avala.Workbench.Presenting;
using Avala.Workbench.Spending;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Usage;

internal interface IUsageRangeViewModel
{
    bool ShowsToday { get; }

    bool ShowsWeek { get; }

    bool ShowsMonth { get; }

    string Caption { get; }

    IUsageWindowViewModel? Total { get; }

    IReadOnlyList<IUsageDayViewModel> Days { get; }

    IRelayCommand ShowTodayCommand { get; }

    IRelayCommand ShowWeekCommand { get; }

    IRelayCommand ShowMonthCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class UsageRangeViewModel : IUsageRangeViewModel
{
    private readonly ObservableCollection<UsageDayViewModel> days = [];

    public UsageRangeViewModel() => Caption = string.Empty;

    public event EventHandler? Chosen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsToday), nameof(ShowsWeek), nameof(ShowsMonth))]
    public partial UsageSpan Span { get; private set; } = UsageSpan.LastSevenDays;

    public bool ShowsToday => Span == UsageSpan.Today;

    public bool ShowsWeek => Span == UsageSpan.LastSevenDays;

    public bool ShowsMonth => Span == UsageSpan.LastThirtyDays;

    [ObservableProperty]
    public partial string Caption { get; private set; }

    [ObservableProperty]
    public partial IUsageWindowViewModel? Total { get; private set; }

    public IReadOnlyList<IUsageDayViewModel> Days => days;

    public void Show(UsageRange range)
    {
        if (range.Span != Span)
        {
            return;
        }

        Total = new UsageWindowViewModel(Label(range.Span), range.Total);
        Caption = range.Span == UsageSpan.Today
            ? "Today, in this computer's time zone"
            : string.Create(CultureInfo.InvariantCulture, $"Since {Day(range.First)}, in this computer's time zone · newest first");
        var busiest = range.Days.Select(day => day.Usage.Tokens.Total()).DefaultIfEmpty(0).Max();
        days.ShowOnly(range.Days.Reverse().Select(day => new UsageDayViewModel(day, busiest, DateOnly.FromDateTime(day.From.DateTime) == range.Today)));
    }

    [RelayCommand]
    private void ShowToday() => Choose(UsageSpan.Today);

    [RelayCommand]
    private void ShowWeek() => Choose(UsageSpan.LastSevenDays);

    [RelayCommand]
    private void ShowMonth() => Choose(UsageSpan.LastThirtyDays);

    private void Choose(UsageSpan span)
    {
        if (span != Span)
        {
            Span = span;
            Chosen?.Invoke(this, EventArgs.Empty);
        }
    }

    public static string Label(UsageSpan span) => span switch
    {
        UsageSpan.Today => "Today",
        UsageSpan.LastSevenDays => "Last 7 days",
        _ => "Last 30 days",
    };

    public static string Day(DateOnly day) => day.ToString("ddd d MMM", CultureInfo.InvariantCulture);
}
