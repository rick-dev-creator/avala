using Avala.Sdk;
using Avala.Sdk.Appearance;
using Avala.Sdk.Telemetry;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Telemetry;

internal interface ITelemetryNoticeViewModel
{
    bool IsShown { get; }

    string Text { get; }

    IAsyncRelayCommand TurnOffCommand { get; }

    IRelayCommand DismissCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class TelemetryNoticeViewModel(ITelemetry telemetry, IOnboarding onboarding) : ITelemetryNoticeViewModel, IActivatable
{
    private bool claimed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TurnOffCommand), nameof(DismissCommand))]
    public partial bool IsShown { get; private set; }

    public string Text => TelemetryPhrases.Notice;

    public Task Claiming { get; private set; } = Task.CompletedTask;

    public void Activate()
    {
        if (!claimed)
        {
            claimed = true;
            Claiming = ClaimAsync(CancellationToken.None);
        }
    }

    public void Deactivate()
    {
    }

    private async Task ClaimAsync(CancellationToken cancellationToken) =>
        IsShown = await onboarding.IsDoneAsync(cancellationToken) && await telemetry.ClaimNoticeAsync(cancellationToken);

    [RelayCommand(CanExecute = nameof(IsShown))]
    private async Task TurnOffAsync(CancellationToken cancellationToken)
    {
        _ = await telemetry.TurnAsync(false, cancellationToken);
        IsShown = false;
    }

    [RelayCommand(CanExecute = nameof(IsShown))]
    private void Dismiss() => IsShown = false;
}
