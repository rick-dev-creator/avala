using System.Collections.ObjectModel;
using System.Globalization;
using Avala.Components.Privacy;
using Avala.Fleet.Connecting;
using Avala.Sdk;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Fleet.Settings;

internal interface IFleetSettingsViewModel
{
    string Server { get; set; }

    string KeyBox { get; set; }

    bool IsConnected { get; }

    bool IsDisconnected { get; }

    bool IsPaused { get; }

    string State { get; }

    Personal Instance { get; }

    string Waiting { get; }

    string LastDelivery { get; }

    string Unanswered { get; }

    bool HasUnanswered { get; }

    string Problem { get; }

    bool HasProblem { get; }

    string Dropped { get; }

    bool HasDropped { get; }

    IReadOnlyList<IFleetRejectionRowViewModel> Rejections { get; }

    bool HasRejections { get; }

    IAsyncRelayCommand ConnectCommand { get; }

    IAsyncRelayCommand TestCommand { get; }

    IAsyncRelayCommand RetryCommand { get; }

    IAsyncRelayCommand TogglePauseCommand { get; }

    IAsyncRelayCommand DisconnectCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class FleetSettingsViewModel(FleetConnection connection, IUiDispatcher ui) : IFleetSettingsViewModel, IActivatable
{
    private readonly ObservableCollection<IFleetRejectionRowViewModel> rejections = [];

    private CancellationTokenSource? watching;

    [ObservableProperty]
    public partial string Server { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string KeyBox { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsConnected { get; private set; }

    [ObservableProperty]
    public partial bool IsDisconnected { get; private set; } = true;

    [ObservableProperty]
    public partial bool IsPaused { get; private set; }

    [ObservableProperty]
    public partial string State { get; private set; } = "Not connected";

    [ObservableProperty]
    public partial Personal Instance { get; private set; } = Personal.None;

    [ObservableProperty]
    public partial string Waiting { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string LastDelivery { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Unanswered { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasUnanswered { get; private set; }

    [ObservableProperty]
    public partial string Problem { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasProblem { get; private set; }

    [ObservableProperty]
    public partial string Dropped { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasDropped { get; private set; }

    [ObservableProperty]
    public partial bool HasRejections { get; private set; }

    public IReadOnlyList<IFleetRejectionRowViewModel> Rejections => rejections;

    public Task Watching { get; private set; } = Task.CompletedTask;

    public void Activate()
    {
        var started = new CancellationTokenSource();
        var stopped = Interlocked.Exchange(ref watching, started);
        stopped?.Cancel();
        stopped?.Dispose();
        Watching = WatchAsync(started.Token);
    }

    public void Deactivate()
    {
        var stopped = Interlocked.Exchange(ref watching, null);
        stopped?.Cancel();
        stopped?.Dispose();
    }

    [RelayCommand]
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var key = KeyBox;
        KeyBox = string.Empty;
        await ShowAsync(await connection.ConnectAsync(Server, key, cancellationToken), cancellationToken);
    }

    [RelayCommand]
    private async Task TestAsync(CancellationToken cancellationToken) =>
        await ShowAsync(await connection.TestAsync(cancellationToken), cancellationToken);

    [RelayCommand]
    private async Task RetryAsync(CancellationToken cancellationToken) =>
        await ShowAsync(await connection.RetryAsync(cancellationToken), cancellationToken);

    [RelayCommand]
    private async Task TogglePauseAsync(CancellationToken cancellationToken) =>
        await ApplyAsync(await connection.PauseAsync(!IsPaused, cancellationToken), cancellationToken);

    [RelayCommand]
    private async Task DisconnectAsync(CancellationToken cancellationToken) =>
        await ShowAsync(await connection.DisconnectAsync(cancellationToken), cancellationToken);

    private async Task WatchAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var status in connection.WatchAsync(cancellationToken))
            {
                await ApplyAsync(status, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            Problem = string.Empty;
        }
    }

    private async Task ShowAsync(ConnectOutcome outcome, CancellationToken cancellationToken)
    {
        var status = await connection.StatusAsync(cancellationToken);
        await ApplyAsync(status, cancellationToken);
        await ui.InvokeAsync(
            () =>
            {
                Problem = Explained(outcome);
                HasProblem = Problem.Length > 0;
            },
            cancellationToken);
    }

    private Task ApplyAsync(FleetStatus status, CancellationToken cancellationToken) =>
        ui.InvokeAsync(
            () =>
            {
                IsConnected = status.State != FleetState.NotConnected;
                IsDisconnected = !IsConnected;
                IsPaused = status.State == FleetState.Paused;
                State = Described(status.State);
                Instance = Personal.AccountName(status.InstanceName);
                Server = status.Server.Length > 0 ? status.Server : Server;
                Waiting = status.Waiting == 1 ? "1 event waiting" : string.Create(CultureInfo.InvariantCulture, $"{status.Waiting} events waiting");
                LastDelivery = status.LastDelivery.Match(at => $"Last delivered {at.ToLocalTime():g}", () => "Nothing delivered yet");
                Unanswered = status.UnansweredSince.Match(at => $"The server has not answered since {at.ToLocalTime():g}; Avala keeps trying", () => string.Empty);
                HasUnanswered = status.UnansweredSince.IsSome && status.State == FleetState.Connected;
                Dropped = string.Create(CultureInfo.InvariantCulture, $"{status.Dropped} events this server did not understand were dropped after 30 days; update the server");
                HasDropped = status.Dropped > 0;
                rejections.Clear();

                foreach (var note in status.Rejections)
                {
                    rejections.Add(new FleetRejectionRowViewModel(note));
                }

                HasRejections = rejections.Count > 0;
            },
            cancellationToken).AsTask();

    private static string Described(FleetState state) => state switch
    {
        FleetState.Connected => "Connected",
        FleetState.Paused => "Paused: nothing is recorded or sent",
        FleetState.KeyRefused => "The server refused this instance's key",
        FleetState.KeychainUnavailable => "The key could not be read from the keychain",
        _ => "Not connected",
    };

    private static string Explained(ConnectOutcome outcome) => outcome switch
    {
        ConnectOutcome.Connected => string.Empty,
        ConnectOutcome.InvalidAddress => "Type the server's address, such as https://fleet.example",
        ConnectOutcome.InsecureAddress => "The key is only sent over https, or over http to this computer",
        ConnectOutcome.NotAKey => "That is not an instance key: it starts with avk_ and has 47 characters",
        ConnectOutcome.NoKeychain => "This system has no keychain Avala can use, so the key cannot be kept",
        ConnectOutcome.KeychainFailed => "The keychain did not keep or give back the key",
        ConnectOutcome.Unreachable => "The server did not answer",
        ConnectOutcome.KeyRefused => "The server refused the key",
        ConnectOutcome.Disconnected => string.Empty,
        ConnectOutcome.DisconnectedKeyKept => "Disconnected, but the keychain could not be reached, so it may still hold the key for this server",
        ConnectOutcome.KeyNotRemoved => "The keychain did not remove the key, so this instance stays connected",
        _ => "Not connected",
    };
}

internal interface IFleetRejectionRowViewModel
{
    string Kind { get; }

    string Reason { get; }

    string When { get; }
}

internal sealed class FleetRejectionRowViewModel(Recording.RejectionNote note) : IFleetRejectionRowViewModel
{
    public string Kind => note.Kind;

    public string Reason => note.Reason;

    public string When => note.At.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
}
