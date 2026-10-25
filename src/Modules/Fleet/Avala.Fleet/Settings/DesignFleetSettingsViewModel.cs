using Avala.Components.Privacy;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Fleet.Settings;

internal sealed class DesignFleetSettingsViewModel : IFleetSettingsViewModel
{
    public string Server { get; set; } = "https://fleet.example";

    public string KeyBox { get; set; } = string.Empty;

    public bool IsConnected { get; init; } = true;

    public bool IsDisconnected => !IsConnected;

    public bool IsPaused { get; init; }

    public string State { get; init; } = "Connected";

    public Personal Instance { get; init; } = Personal.AccountName("desktop");

    public string Waiting { get; init; } = "3 events waiting";

    public string LastDelivery { get; init; } = "Last delivered 25/10/2026 12:01";

    public string Unanswered { get; init; } = string.Empty;

    public bool HasUnanswered => Unanswered.Length > 0;

    public string Problem { get; init; } = string.Empty;

    public bool HasProblem => Problem.Length > 0;

    public string Dropped { get; init; } = string.Empty;

    public bool HasDropped => Dropped.Length > 0;

    public IReadOnlyList<IFleetRejectionRowViewModel> Rejections { get; init; } = [new DesignFleetRejectionRowViewModel()];

    public bool HasRejections => Rejections.Count > 0;

    public IAsyncRelayCommand ConnectCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand TestCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand RetryCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand TogglePauseCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand DisconnectCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);
}

internal sealed class DesignFleetRejectionRowViewModel : IFleetRejectionRowViewModel
{
    public string Kind { get; init; } = "usageReported";

    public string Reason { get; init; } = "a cost has at most 6 decimals";

    public string When { get; init; } = "25/10/2026 12:00";
}
