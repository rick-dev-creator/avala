using Avala.Sdk.Telemetry;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Telemetry;

internal interface ITelemetryViewModel
{
    string Status { get; }

    bool IsOn { get; }

    bool CanTurn { get; }

    bool HasInstallId { get; }

    string InstallId { get; }

    string Preview { get; }

    IAsyncRelayCommand ToggleCommand { get; }

    IAsyncRelayCommand ResetInstallIdCommand { get; }

    Task LoadAsync(CancellationToken cancellationToken);
}

[INotifyPropertyChanged]
internal sealed partial class TelemetryViewModel(ITelemetry telemetry) : ITelemetryViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status), nameof(IsOn), nameof(CanTurn), nameof(HasInstallId), nameof(InstallId))]
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand), nameof(ResetInstallIdCommand))]
    public partial TelemetryState State { get; private set; } = TelemetryState.NotConfigured;

    [ObservableProperty]
    public partial string Preview { get; private set; } = string.Empty;

    public string Status => TelemetryPhrases.Status(State);

    public bool IsOn => State.Status == TelemetryStatus.On;

    public bool CanTurn => State.Status is TelemetryStatus.On or TelemetryStatus.Off;

    public bool HasInstallId => State.InstallId.IsSome;

    public string InstallId => State.InstallId.Match(id => id, () => string.Empty);

    public async Task LoadAsync(CancellationToken cancellationToken) => await ShowAsync(await telemetry.ReadAsync(cancellationToken), cancellationToken);

    [RelayCommand(CanExecute = nameof(CanTurn))]
    private async Task ToggleAsync(CancellationToken cancellationToken) =>
        await ShowAsync(await telemetry.TurnAsync(!IsOn, cancellationToken), cancellationToken);

    [RelayCommand(CanExecute = nameof(HasInstallId))]
    private async Task ResetInstallIdAsync(CancellationToken cancellationToken) =>
        await ShowAsync(await telemetry.ResetInstallIdAsync(cancellationToken), cancellationToken);

    private async Task ShowAsync(TelemetryState state, CancellationToken cancellationToken)
    {
        State = state;
        Preview = await telemetry.PreviewAsync(cancellationToken);
    }
}

internal static class TelemetryPhrases
{
    public const string Notice = "Avala sends its version, your OS and a random install ID to Aptabase, at most once a day.";

    public const string Sent =
        "Once when Avala is first started and at most once a day after that: the version, the operating system, its version and architecture, under a random install ID made on this computer. Never a repository, job, prompt, path, account, connection or cost.";

    public static string Status(TelemetryState state) => state.Status switch
    {
        TelemetryStatus.On => "On. Only events like the one below are sent, nothing else.",
        TelemetryStatus.Off => "Off. Nothing is sent.",
        TelemetryStatus.OffByEnvironment => $"Off because {state.OptedOutBy.Match(variable => variable, () => "DO_NOT_TRACK")} is set in Avala's environment. Nothing is sent.",
        _ => "Not configured in this build: nothing is ever sent.",
    };
}
