using System.Collections.ObjectModel;
using System.Globalization;
using Avala.Components.Privacy;
using Avala.Fleet.Contracts;
using Avala.Sdk;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.FleetActivity;

internal interface IFleetActivityViewModel
{
    string Notice { get; }

    bool HasNotice { get; }

    IReadOnlyList<IFleetRepositoryRowViewModel> Repositories { get; }

    string Unattributed { get; }

    bool HasUnattributed { get; }

    IAsyncRelayCommand RefreshCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class FleetActivityViewModel(IFleetActivity activity, IUiDispatcher ui) : IFleetActivityViewModel, IPage, IActivatable
{
    private readonly ObservableCollection<IFleetRepositoryRowViewModel> repositories = [];

    public string Title => "Fleet";

    public string Icon => "IconFleet";

    public IReadOnlyList<IFleetRepositoryRowViewModel> Repositories => repositories;

    [ObservableProperty]
    public partial string Notice { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasNotice { get; private set; }

    [ObservableProperty]
    public partial string Unattributed { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasUnattributed { get; private set; }

    public Task Loading { get; private set; } = Task.CompletedTask;

    public void Activate() => Loading = RefreshAsync(CancellationToken.None);

    public void Deactivate()
    {
    }

    public static string Spent(FleetTokens tokens, IReadOnlyList<FleetCost> costs) =>
        string.Join(
            " · ",
            [
                string.Create(CultureInfo.CurrentCulture, $"{tokens.Input:N0} in, {tokens.Output:N0} out, {tokens.CacheRead:N0} cache read"),
                .. costs.Select(cost => string.Create(CultureInfo.CurrentCulture, $"{Amount(cost.Amount)} {cost.Currency} {(cost.Basis == "billed" ? "billed" : "at API prices")}")),
            ]);

    public static string Counted(long count, string noun) =>
        string.Create(CultureInfo.CurrentCulture, $"{count:N0} {noun}{(count == 1 ? string.Empty : "s")}");

    private static string Amount(decimal amount) =>
        amount.ToString(amount >= 1 || amount == 0 ? "0.##" : "0.####", CultureInfo.CurrentCulture);

    public static string Said(FleetReadState state) => state switch
    {
        FleetReadState.NotConnected => "This instance is not connected to a fleet server. Connect it in Settings › Fleet.",
        FleetReadState.Paused => "The fleet is paused in Settings › Fleet.",
        FleetReadState.KeyRefused => "The fleet server refused this instance's key. Check it in Settings › Fleet.",
        FleetReadState.Unreachable => "The fleet server did not answer.",
        _ => string.Empty,
    };

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var view = await activity.RepositoriesAsync(cancellationToken);

        await ui.InvokeAsync(
            () =>
            {
                Notice = view.State == FleetReadState.Read && view.Repositories.Count == 0 ? "No repository has been worked on in this fleet yet." : Said(view.State);
                HasNotice = Notice.Length > 0;
                repositories.Clear();

                foreach (var repository in view.Repositories)
                {
                    repositories.Add(new FleetRepositoryRowViewModel(repository, activity, ui));
                }

                Unattributed = string.Create(CultureInfo.CurrentCulture, $"{view.UnattributedJobs} jobs whose repository is not known yet");
                HasUnattributed = view.UnattributedJobs > 0;
            },
            cancellationToken);
    }
}

internal interface IFleetRepositoryRowViewModel
{
    Personal Key { get; }

    string Summary { get; }

    string Spending { get; }

    bool IsExpanded { get; }

    string Notice { get; }

    IReadOnlyList<IFleetJobRowViewModel> Jobs { get; }

    IAsyncRelayCommand ToggleCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class FleetRepositoryRowViewModel(FleetRepository repository, IFleetActivity activity, IUiDispatcher ui) : IFleetRepositoryRowViewModel
{
    private readonly ObservableCollection<IFleetJobRowViewModel> jobs = [];

    public Personal Key { get; } = Personal.AccountName(repository.Key);

    public string Summary { get; } = string.Create(
        CultureInfo.CurrentCulture,
        $"{FleetActivityViewModel.Counted(repository.Jobs, "job")} on {FleetActivityViewModel.Counted(repository.Instances, "instance")} · last {repository.LastActivityAt.ToLocalTime():g}");

    public string Spending { get; } = FleetActivityViewModel.Spent(repository.Tokens, repository.Costs);

    public IReadOnlyList<IFleetJobRowViewModel> Jobs => jobs;

    [ObservableProperty]
    public partial bool IsExpanded { get; private set; }

    [ObservableProperty]
    public partial string Notice { get; private set; } = string.Empty;

    [RelayCommand]
    private async Task ToggleAsync(CancellationToken cancellationToken)
    {
        if (IsExpanded)
        {
            IsExpanded = false;

            return;
        }

        var view = await activity.JobsAsync(repository.Key, cancellationToken);

        await ui.InvokeAsync(
            () =>
            {
                jobs.Clear();

                foreach (var job in view.Jobs)
                {
                    jobs.Add(new FleetJobRowViewModel(job));
                }

                Notice = FleetActivityViewModel.Said(view.State);
                IsExpanded = true;
            },
            cancellationToken);
    }
}

internal interface IFleetJobRowViewModel
{
    Personal Instance { get; }

    string State { get; }

    string Spending { get; }
}

internal sealed class FleetJobRowViewModel(FleetJob job) : IFleetJobRowViewModel
{
    public Personal Instance { get; } = Personal.AccountName(job.InstanceName);

    public string State { get; } = string.Join(
        " · ",
        new[]
        {
            job.Status,
            job.Hold.Length > 0 ? $"held: {job.Hold}" : string.Empty,
            job.Attempts > 0 ? string.Create(CultureInfo.CurrentCulture, $"{FleetActivityViewModel.Counted(job.Attempts, "attempt")}, last {job.LastOutcome}") : string.Empty,
            job.LastActivityAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
        }.Where(part => part.Length > 0));

    public string Spending { get; } = FleetActivityViewModel.Spent(job.Tokens, job.Costs);
}
