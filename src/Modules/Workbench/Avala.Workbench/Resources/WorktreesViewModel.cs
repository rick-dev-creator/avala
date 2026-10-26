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

    bool CanOpen { get; }

    IRelayCommand OpenCommand { get; }
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
internal sealed partial class WorktreesViewModel(WorktreeReader reader, LiveFeed feed, JobFocus focus) : IWorktreesViewModel, IPage, IActivatable, IPresentation, IDisposable
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
        Freed = WorktreePagePhrases.Freed(freed.Freed.Count, freed.Freed.Sum(release => release.Bytes), freed.LeftTheirBranch);
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
        var first = today.AddDays(1 - count);
        recovered.ShowOnly(state.Report.History
            .Where(recorded => DateOnly.FromDateTime(recorded.At.ToLocalTime().DateTime) >= first && recorded.Moment.IsSome)
            .GroupBy(recorded => recorded.Moment.Match(moment => moment, () => WorktreeMoment.Requested))
            .OrderByDescending(group => group.Sum(recorded => recorded.Bytes))
            .Select(group => new WorktreeRowViewModel(WorktreePagePhrases.Moment(group.Key), WorktreePagePhrases.Count(group.Count()), WorktreePagePhrases.Bytes(group.Sum(recorded => recorded.Bytes)))));

        return true;
    }

    private void Show(WorktreeState state)
    {
        last = state;
        var states = WorktreePagePhrases.States(state.Folders.Select(folder => folder.Job));
        Space = $"{WorktreePagePhrases.Bytes(state.Folders.Sum(folder => folder.Folder.Bytes))} in {WorktreePagePhrases.Count(state.Folders.Count)}{(states.Length > 0 ? $": {states}" : string.Empty)}";
        Limits = WorktreePagePhrases.Limits(state.Policy, state.Disk);
        Shortage = state.Shortage.Match(WorktreePagePhrases.Shortage, () => string.Empty);
        largest.ShowOnly(state.Folders
            .OrderByDescending(folder => folder.Folder.Bytes)
            .Take(Shown)
            .Select(folder => new WorktreeRowViewModel(WorktreePagePhrases.Title(folder.Job, folder.Folder.Path), WorktreePagePhrases.Build(folder.Folder), WorktreePagePhrases.Bytes(folder.Folder.Bytes)) { Open = Opening(folder.Job.Map(job => job.Job)) }));
        upcoming.ShowOnly(state.Upcoming
            .Take(Shown)
            .Select(next => new WorktreeRowViewModel(
                WorktreePagePhrases.Title(next.Job, next.Due.Path),
                next.Due.LeftItsBranch ? WorktreePagePhrases.LeftItsBranch(next.Due.Moment) : WorktreePagePhrases.Moment(next.Due.Moment),
                next.Due.LeftItsBranch ? WorktreePagePhrases.Kept : WorktreePagePhrases.When(next.Due.Due, state.Now)) { Open = Opening(next.Due.Job) }));
        history.ShowOnly(state.Report.History
            .Take(Kept)
            .Select(recorded => new WorktreeRowViewModel(HistoryTitle(state, recorded), WorktreePagePhrases.Event(recorded), Amounts.Time(recorded.At)) { Open = Opening(recorded.Job) }));
        Lifetime = WorktreePagePhrases.Lifetime(state.Report.History);
        _ = ShowTrends(state);
    }

    private static string HistoryTitle(WorktreeState state, WorktreeEvent recorded) =>
        WorktreePagePhrases.Title(state.Jobs.GetValueOrDefault(recorded.Job).ToOption(), recorded.Path);

    private Option<Action> Opening(Option<Jobs.Contracts.JobId> job) => job.Map<Action>(found => () => focus.Select(found));
}

internal sealed class WorktreeRowViewModel(string title, string detail, string amount) : IWorktreeRowViewModel
{
    public string Title { get; } = title;

    public string Detail { get; } = detail;

    public string Amount { get; } = amount;

    private RelayCommand? open;

    public Option<Action> Open { get; init; }

    public bool CanOpen => Open.IsSome;

    public IRelayCommand OpenCommand => open ??= new RelayCommand(() => Open.Match(action => action, () => () => { })(), () => CanOpen);
}

internal sealed class WorktreeDayViewModel(WorktreeDay day, long peak, bool isToday) : IWorktreeDayViewModel
{
    public string Day { get; } = UsageRangeViewModel.Day(day.Day) + (isToday ? " · today" : string.Empty);

    public double Share { get; } = peak == 0 ? 0 : (double)day.Peak / peak;

    public string Peak { get; } = WorktreePagePhrases.Bytes(day.Peak);

    public string Detail { get; } = WorktreePagePhrases.Day(day);

    public bool IsToday { get; } = isToday;
}
