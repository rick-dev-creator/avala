using System.Collections.ObjectModel;
using Avala.Components.Meters;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Workbench.Following;
using Avala.Workbench.Navigation;
using Avala.Workbench.Presenting;
using Avala.Workbench.Spending;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Usage;

internal interface IUsageLeftViewModel
{
    bool IsShown { get; }

    string Connection { get; }

    string Left { get; }

    double Used { get; }

    bool IsNear { get; }

    string Summary { get; }

    IReadOnlyList<ILimitViewModel> Windows { get; }

    IRelayCommand OpenCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class UsageLeftViewModel(JobAllowance allowance, LiveFeed feed, JobFocus focus, UsageViewModel usage)
    : IUsageLeftViewModel, IRegionAware<JobId>, IActivatable, IPresentation, IDisposable
{
    private readonly ObservableCollection<LimitViewModel> windows = [];
    private Option<JobId> job;

    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    public long Revision => feed.Revision;

    public Task Following => feed.Following;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    public partial bool IsShown { get; private set; }

    [ObservableProperty]
    public partial string Connection { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Left { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial double Used { get; private set; }

    [ObservableProperty]
    public partial bool IsNear { get; private set; }

    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    public IReadOnlyList<ILimitViewModel> Windows => windows;

    public void Activate() => feed.Start(_ => ValueTask.FromResult(job.Bind(allowance.Of)), Show);

    public void Deactivate() => feed.Stop();

    public void Dispose() => feed.Dispose();

    public void OnRegionContextChanged(Option<JobId> context)
    {
        job = context;
        feed.Refresh();
    }

    private void Show(Option<ConnectionSpend> spend)
    {
        var shown = spend.Match(found => found.Limits.Count > 0 ? found : Option<ConnectionSpend>.None, () => Option<ConnectionSpend>.None);
        var holdAt = shown.Bind(found => found.Caps).Bind(caps => caps.HoldAtLimit);
        var readings = shown.Match(found => found.Limits, () => (IReadOnlyList<LimitReading>)[]);
        windows.ShowInPlace(
            readings,
            (window, reading) => window.Window == reading.Limit.Window,
            reading => new LimitViewModel(reading, holdAt),
            (window, reading) => window.Show(reading, holdAt));
        var most = readings.OrderByDescending(reading => reading.Used).FirstOrDefault();
        Connection = shown.Match(found => found.Usage.Connection.Value, () => string.Empty);
        Used = most is null ? 0 : Math.Clamp(most.Used, 0, 1);
        Left = most is null ? string.Empty : UsageLeftPhrases.Left(Used);
        IsNear = windows.Any(window => window.IsNear) || (most is not null && Used >= 1 - MeterViewModel.AttentionMargin);
        Summary = UsageLeftPhrases.Summary(Connection, windows);
        IsShown = shown.IsSome;
    }

    [RelayCommand(CanExecute = nameof(IsShown))]
    private void Open()
    {
        usage.Focus(Connection);
        focus.Show(usage);
    }
}

internal static class UsageLeftPhrases
{
    public static string Left(double used) => $"{Amounts.Percent(1 - used)} usage left";

    public static string Summary(string connection, IEnumerable<ILimitViewModel> windows) =>
        string.Join(" · ", windows.Select(window => $"{window.Label} {window.UsedText} used, {window.Resets}").Prepend($"Usage left on {connection}"));
}
