using System.Collections.ObjectModel;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Workbench.Following;
using Avala.Workbench.Navigation;
using Avala.Workbench.Presenting;
using Avala.Workbench.Spending;
using Avala.Workbench.Upkeep;
using Avala.Workbench.Usage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Resources;

internal interface IWorktreesViewModel
{
    string Title { get; }

    string Scope { get; }

    string Space { get; }

    string Limits { get; }

    string Shortage { get; }

    string Freed { get; }

    string Lifetime { get; }

    bool ShowsWeek { get; }

    bool ShowsMonth { get; }

    IReadOnlyList<IWorktreeRowViewModel> Largest { get; }

    IReadOnlyList<IWorktreeRowViewModel> Upcoming { get; }

    IReadOnlyList<IWorktreeDayViewModel> Days { get; }

    IReadOnlyList<IWorktreeRowViewModel> Recovered { get; }

    IReadOnlyList<IWorktreeRowViewModel> History { get; }

    IRelayCommand ShowWeekCommand { get; }

    IRelayCommand ShowMonthCommand { get; }

    IAsyncRelayCommand FreeNowCommand { get; }
}

internal interface IWorktreeRowViewModel
{
    string Title { get; }

    string Detail { get; }

    string Amount { get; }
}

internal interface IWorktreeDayViewModel
{
    string Day { get; }

    double Share { get; }

    string Peak { get; }

    string Detail { get; }

    bool IsToday { get; }
}

internal sealed class WorktreesEntry(JobFocus focus, WorktreesViewModel worktrees)
{
    public void Open() => focus.Show(worktrees);
}

[INotifyPropertyChanged]
internal sealed partial class WorktreesViewModel(WorktreeReader reader, LiveFeed feed) : IWorktreesViewModel, IPage, IActivatable, IPresentation, IDisposable
{
    private const int Shown = 5;

    private const int Kept = 20;

    private readonly ObservableCollection<WorktreeRowViewModel> largest = [];
    private readonly ObservableCollection<WorktreeRowViewModel> upcoming = [];
    private readonly ObservableCollection<WorktreeDayViewModel> days = [];
    private readonly ObservableCollection<WorktreeRowViewModel> recovered = [];
    private readonly ObservableCollection<WorktreeRowViewModel> history = [];
    private Option<WorktreeState> last;

    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    public long Revision => feed.Revision;

    public string Title => "Worktrees";

    public string Icon => "IconFolder";

    public string Scope => WorktreePagePhrases.Scope;

    public PagePlacement Placement => PagePlacement.Hidden;

    [ObservableProperty]
    public partial string Space { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Limits { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Shortage { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Freed { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Lifetime { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsWeek), nameof(ShowsMonth))]
    public partial UsageSpan Span { get; private set; } = UsageSpan.LastSevenDays;

    public bool ShowsWeek => Span == UsageSpan.LastSevenDays;

    public bool ShowsMonth => Span == UsageSpan.LastThirtyDays;

    public IReadOnlyList<IWorktreeRowViewModel> Largest => largest;

    public IReadOnlyList<IWorktreeRowViewModel> Upcoming => upcoming;

    public IReadOnlyList<IWorktreeDayViewModel> Days => days;

    public IReadOnlyList<IWorktreeRowViewModel> Recovered => recovered;

    public IReadOnlyList<IWorktreeRowViewModel> History => history;

    public Task Following => feed.Following;

    public void Activate() => feed.Start(token => reader.ReadAsync(feed.Clock.GetUtcNow(), token), Show);

    public void Deactivate() => feed.Stop();

    public void Dispose() => feed.Dispose();

    [RelayCommand]
    private void ShowWeek() => Choose(UsageSpan.LastSevenDays);

    [RelayCommand]
    private void ShowMonth() => Choose(UsageSpan.LastThirtyDays);

    [RelayCommand]
    private async Task FreeNowAsync(CancellationToken cancellationToken)
    {
        var freed = await reader.FreeNowAsync(cancellationToken);
        Freed = WorktreePagePhrases.Freed(freed.Count, freed.Sum(release => release.Bytes));
        feed.Refresh();
    }

    private void Choose(UsageSpan span)
    {
        Span = span;
        _ = last.Match(ShowTrends, () => false);
    }

    private bool ShowTrends(WorktreeState state)
    {
        var count = Span == UsageSpan.LastSevenDays ? 7 : WorktreeReader.Days;
        var today = DateOnly.FromDateTime(state.Now.ToLocalTime().DateTime);
        var known = state.Report.Days.ToDictionary(day => day.Day);
        var range = Enumerable.Range(0, count).Select(back => today.AddDays(-back)).Select(day => known.GetValueOrDefault(day, new WorktreeDay(day, 0, 0, 0, 0, 0))).ToList();
        var peak = range.Select(day => day.Peak).DefaultIfEmpty(0).Max();
        days.ShowOnly(range.Select(day => new WorktreeDayViewModel(day, peak, day.Day == today)));
        var since = new DateTimeOffset(today.AddDays(1 - count).ToDateTime(TimeOnly.MinValue), state.Now.Offset);
        recovered.ShowOnly(state.Report.History
            .Where(recorded => recorded.At >= since && recorded.Moment.IsSome)
            .GroupBy(recorded => recorded.Moment.Match(moment => moment, () => WorktreeMoment.Requested))
            .OrderByDescending(group => group.Sum(recorded => recorded.Bytes))
            .Select(group => new WorktreeRowViewModel(WorktreePagePhrases.Moment(group.Key), WorktreePagePhrases.Count(group.Count()), WorktreePagePhrases.Bytes(group.Sum(recorded => recorded.Bytes)))));

        return true;
    }

    private void Show(WorktreeState state)
    {
        last = state;
        Space = $"{WorktreePagePhrases.Bytes(state.Folders.Sum(folder => folder.Folder.Bytes))} in {WorktreePagePhrases.Count(state.Folders.Count)}";
        Limits = WorktreePagePhrases.Limits(state.Policy, state.Disk);
        Shortage = state.Shortage.Match(WorktreePagePhrases.Shortage, () => string.Empty);
        largest.ShowOnly(state.Folders
            .OrderByDescending(folder => folder.Folder.Bytes)
            .Take(Shown)
            .Select(folder => new WorktreeRowViewModel(WorktreePagePhrases.Title(folder.Job, folder.Folder.Path), WorktreePagePhrases.Build(folder.Folder), WorktreePagePhrases.Bytes(folder.Folder.Bytes))));
        upcoming.ShowOnly(state.Upcoming
            .Take(Shown)
            .Select(next => new WorktreeRowViewModel(WorktreePagePhrases.Title(next.Job, next.Due.Path), WorktreePagePhrases.Moment(next.Due.Moment), WorktreePagePhrases.When(next.Due.Due, state.Now))));
        history.ShowOnly(state.Report.History
            .Take(Kept)
            .Select(recorded => new WorktreeRowViewModel(Path.GetFileName(Path.TrimEndingDirectorySeparator(recorded.Path)), WorktreePagePhrases.Event(recorded), Amounts.Time(recorded.At))));
        Lifetime = WorktreePagePhrases.Lifetime(state.Report.History);
        _ = ShowTrends(state);
    }
}

internal sealed class WorktreeRowViewModel(string title, string detail, string amount) : IWorktreeRowViewModel
{
    public string Title { get; } = title;

    public string Detail { get; } = detail;

    public string Amount { get; } = amount;
}

internal sealed class WorktreeDayViewModel(WorktreeDay day, long peak, bool isToday) : IWorktreeDayViewModel
{
    public string Day { get; } = UsageRangeViewModel.Day(day.Day) + (isToday ? " · today" : string.Empty);

    public double Share { get; } = peak == 0 ? 0 : (double)day.Peak / peak;

    public string Peak { get; } = WorktreePagePhrases.Bytes(day.Peak);

    public string Detail { get; } = WorktreePagePhrases.Day(day);

    public bool IsToday { get; } = isToday;
}
