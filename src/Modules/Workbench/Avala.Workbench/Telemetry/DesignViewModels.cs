using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Telemetry;

internal sealed class DesignTelemetryViewModel : ITelemetryViewModel
{
    public string Status { get; init; } = "On. Only events like the one below are sent, nothing else.";

    public bool IsOn { get; init; } = true;

    public bool CanTurn { get; init; } = true;

    public bool HasInstallId => InstallId.Length > 0;

    public string InstallId { get; init; } = "3f2b8c1e-7d4a-4e09-9b6f-52c1a0d8e7f4";

    public string Preview { get; init; } =
        """
        {
          "timestamp": "2026-10-12T09:30:00.000Z",
          "sessionId": "3f2b8c1e-7d4a-4e09-9b6f-52c1a0d8e7f4",
          "eventName": "active",
          "systemProps": {
            "osName": "Linux",
            "osVersion": "6.10.2",
            "appVersion": "0.1.0-beta.1",
            "sdkVersion": "avala"
          },
          "props": {
            "architecture": "x64"
          }
        }
        """;

    public IAsyncRelayCommand ToggleCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand ResetInstallIdCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class DesignTelemetryNoticeViewModel : ITelemetryNoticeViewModel
{
    public bool IsShown => true;

    public string Text => TelemetryPhrases.Notice;

    public IAsyncRelayCommand TurnOffCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IRelayCommand DismissCommand { get; } = new RelayCommand(() => { });
}
