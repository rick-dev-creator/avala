using System.Collections.ObjectModel;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Workbench.Automation;
using Avala.Workbench.Following;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Triggers;

internal interface ITriggersViewModel
{
    string Title { get; }

    string Scope { get; }

    string Endpoint { get; }

    string Tunnel { get; }

    string Notice { get; }

    string Error { get; }

    IReadOnlyList<ITriggerItemViewModel> Triggers { get; }

    IReadOnlyList<string> Files { get; }

    IReadOnlyList<string> Deliveries { get; }

    IAsyncRelayCommand<ITriggerItemViewModel> RunNowCommand { get; }

    IAsyncRelayCommand<ITriggerItemViewModel> ToggleCommand { get; }

    IAsyncRelayCommand ReloadCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class TriggersViewModel(TriggerControls controls, LiveFeed feed) : ITriggersViewModel, IPage, IActivatable, IPresentation, IDisposable
{
    private readonly ObservableCollection<ITriggerItemViewModel> triggers = [];
    private readonly ObservableCollection<string> files = [];
    private readonly ObservableCollection<string> deliveries = [];

    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    public long Revision => feed.Revision;

    public string Title => "Triggers";

    public string Icon => "IconTriggers";

    public string Tunnel => TriggerPhrases.Tunnel;

    [ObservableProperty]
    public partial string Scope { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Endpoint { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Notice { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Error { get; private set; } = string.Empty;

    public IReadOnlyList<ITriggerItemViewModel> Triggers => triggers;

    public IReadOnlyList<string> Files => files;

    public IReadOnlyList<string> Deliveries => deliveries;

    public Task Following => feed.Following;

    public void Activate() => feed.Start(controls.ReadAsync, Show);

    public void Deactivate() => feed.Stop();

    public void Dispose() => feed.Dispose();

    [RelayCommand(CanExecute = nameof(Present))]
    private async Task RunNowAsync(ITriggerItemViewModel? trigger, CancellationToken cancellationToken)
    {
        var fired = await controls.RunNowAsync(trigger!.Id, cancellationToken);
        (Notice, Error) = fired.Match(run => (TriggerPhrases.Fired(run), string.Empty), error => (string.Empty, TriggerPhrases.Error(error)));
        feed.Refresh();
    }

    [RelayCommand(CanExecute = nameof(Present))]
    private async Task ToggleAsync(ITriggerItemViewModel? trigger, CancellationToken cancellationToken)
    {
        var changed = await controls.EnableAsync(trigger!.Id, !trigger.IsEnabled, cancellationToken);
        (Notice, Error) = changed.Match(_ => (string.Empty, string.Empty), error => (string.Empty, TriggerPhrases.Error(error)));
        feed.Refresh();
    }

    [RelayCommand]
    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        _ = await controls.ReloadAsync(cancellationToken);
        (Notice, Error) = ("Reloaded the trigger files.", string.Empty);
        feed.Refresh();
    }

    private static bool Present(ITriggerItemViewModel? trigger) => trigger is not null;

    private void Show(TriggersState state)
    {
        Scope = TriggerPhrases.Scope(state.Zone);
        Endpoint = TriggerPhrases.Endpoint(state.Endpoint);
        triggers.ShowOnly(state.Triggers.Select(view => new TriggerItemViewModel(view, state.Zone)));
        files.ShowOnly(state.Files.Select(TriggerPhrases.File));
        deliveries.ShowOnly(state.Deliveries.Select(delivery => TriggerPhrases.Delivery(delivery, state.Zone)));
    }
}
