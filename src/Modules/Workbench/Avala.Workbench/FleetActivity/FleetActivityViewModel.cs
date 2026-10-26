using System.Collections.ObjectModel;
using Avala.Components.Privacy;
using Avala.Fleet.Contracts;
using Avala.Sdk;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.FleetActivity;

internal interface IFleetActivityViewModel
{
    IFleetRangeViewModel Range { get; }

    string Notice { get; }

    bool HasNotice { get; }

    bool HasFigures { get; }

    string ZoneNote { get; }

    bool HasZoneNote { get; }

    Personal Scope { get; }

    bool IsScoped { get; }

    bool AreNamesShown { get; }

    string NamesAction { get; }

    IReadOnlyList<IFleetTileViewModel> Tiles { get; }

    IFleetTrendViewModel Trend { get; }

    IReadOnlyList<IFleetShareListViewModel> Lists { get; }

    IFleetRepositoryJobsViewModel Jobs { get; }

    IAsyncRelayCommand RefreshCommand { get; }

    IAsyncRelayCommand ClearScopeCommand { get; }

    IRelayCommand ToggleNamesCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class FleetActivityViewModel : IFleetActivityViewModel, IPage, IActivatable
{
    private readonly IFleetActivity activity;

    private readonly IUiDispatcher ui;

    private readonly TimeProvider clock;

    private readonly FleetRangeViewModel range;

    private readonly FleetTrendViewModel trend = new();

    private readonly FleetShareListViewModel repositories = new("Repositories");

    private readonly FleetShareListViewModel instances = new("Instances");

    private readonly FleetShareListViewModel models = new("Models");

    private readonly FleetRepositoryJobsViewModel jobs = new();

    private readonly ObservableCollection<IFleetTileViewModel> tiles = [];

    private FleetUsage usage = FleetUsage.Empty;

    private FleetComparison comparison = FleetComparison.None;

    private Option<string> scope = Option<string>.None;

    private int loads;

    public FleetActivityViewModel(IFleetActivity activity, IUiDispatcher ui, TimeProvider clock)
    {
        this.activity = activity;
        this.ui = ui;
        this.clock = clock;
        range = new FleetRangeViewModel(Today);
        range.Chosen += (_, _) => Loading = LoadAsync(CancellationToken.None);
        trend.MetricChanged += (_, _) => ShowLists();
    }

    public string Title => "Fleet";

    public string Icon => "IconFleet";

    public IFleetRangeViewModel Range => range;

    public IReadOnlyList<IFleetTileViewModel> Tiles => tiles;

    public IFleetTrendViewModel Trend => trend;

    public IReadOnlyList<IFleetShareListViewModel> Lists => [repositories, instances, models];

    public IFleetRepositoryJobsViewModel Jobs => jobs;

    [ObservableProperty]
    public partial string ZoneNote { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasZoneNote { get; private set; }

    [ObservableProperty]
    public partial string Notice { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasNotice { get; private set; }

    [ObservableProperty]
    public partial bool HasFigures { get; private set; }

    [ObservableProperty]
    public partial Personal Scope { get; private set; } = Personal.None;

    [ObservableProperty]
    public partial bool IsScoped { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NamesAction))]
    public partial bool AreNamesShown { get; private set; }

    public string NamesAction => AreNamesShown ? "Hide names" : "Show names";

    public Task Loading { get; private set; } = Task.CompletedTask;

    public void Activate()
    {
        range.Refresh();
        Loading = LoadAsync(CancellationToken.None);
    }

    public void Deactivate() => AreNamesShown = false;

    [RelayCommand]
    private void ToggleNames() => AreNamesShown = !AreNamesShown;

    [RelayCommand]
    private Task RefreshAsync(CancellationToken cancellationToken)
    {
        range.Refresh();

        return LoadAsync(cancellationToken);
    }

    [RelayCommand]
    private Task ClearScopeAsync(CancellationToken cancellationToken)
    {
        scope = Option<string>.None;

        return LoadAsync(cancellationToken);
    }

    private Task ChooseAsync(string repository)
    {
        scope = scope.Match(chosen => chosen == repository, () => false) ? Option<string>.None : repository;

        return LoadAsync(CancellationToken.None);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var load = Interlocked.Increment(ref loads);
        var zone = activity.Zone;
        var chosen = range.Current;
        var scoped = scope;
        var before = FleetRanges.Before(chosen);
        var asked = activity.UsageAsync(new FleetUsageQuery(chosen.From, chosen.To, chosen.Step, scoped), cancellationToken).AsTask();
        var earlier = activity.UsageAsync(new FleetUsageQuery(before.From, before.To, before.Step, scoped), cancellationToken).AsTask();
        var view = await asked;
        var previous = await earlier;
        var repositoryJobs = await scoped.Match(
            key => activity.JobsAsync(key, cancellationToken).AsTask(),
            () => Task.FromResult(FleetJobsView.Of(FleetReadState.Read)));

        await ui.InvokeAsync(
            () =>
            {
                if (load != Volatile.Read(ref loads))
                {
                    return;
                }

                ZoneNote = zone.InUtc ? "Dates are in UTC: this computer's time zone has no name the fleet server knows." : string.Empty;
                HasZoneNote = zone.InUtc;
                Notice = FleetFigures.Said(view.State);
                HasNotice = Notice.Length > 0;
                HasFigures = view.State == FleetReadState.Read;
                usage = view.Usage;
                comparison = new FleetComparison(
                    previous.State == FleetReadState.Read ? previous.Usage : Option<FleetUsage>.None,
                    FleetRanges.Previous(chosen));
                tiles.ShowOnly(FleetTileViewModel.Of(usage, comparison));
                trend.Show(usage, comparison, chosen.Step);
                ShowLists();
                IsScoped = scoped.IsSome;
                Scope = scoped.Match(key => Personal.AccountName(FleetFigures.Repository(key)), () => Personal.None);
                scoped.Match(
                    key =>
                    {
                        jobs.Show(key, repositoryJobs);

                        return true;
                    },
                    () => false);
            },
            cancellationToken);
    }

    private void ShowLists()
    {
        repositories.Show(usage.Repositories, comparison.Before.Map(before => before.Repositories), trend.ShowsSpend, usage.Reference, scope, ChooseAsync);
        instances.Show(usage.Instances, comparison.Before.Map(before => before.Instances), trend.ShowsSpend, usage.Reference, scope, ChooseAsync);
        models.Show(usage.Models, comparison.Before.Map(before => before.Models), trend.ShowsSpend, usage.Reference, scope, ChooseAsync);
    }

    private DateOnly Today()
    {
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(activity.Zone.Id, out var found) ? found : TimeZoneInfo.Utc;

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
    }
}
