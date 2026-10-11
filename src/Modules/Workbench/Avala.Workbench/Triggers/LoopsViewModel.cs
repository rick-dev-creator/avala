using System.Collections.ObjectModel;
using Avala.Autopilot.Contracts;
using Avala.Sdk;
using Avala.Workbench.Automation;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Triggers;

internal interface ILoopsViewModel
{
    IReadOnlyList<ILoopItemViewModel> Loops { get; }

    string Error { get; }

    IAsyncRelayCommand<ILoopItemViewModel> PauseCommand { get; }

    IAsyncRelayCommand<ILoopItemViewModel> ResumeCommand { get; }

    IAsyncRelayCommand<ILoopItemViewModel> StopCommand { get; }

    void Show(IReadOnlyList<LoopDigest> loops, TimeZoneInfo zone);
}

[INotifyPropertyChanged]
internal sealed partial class LoopsViewModel(LoopControls controls) : ILoopsViewModel
{
    private readonly ObservableCollection<ILoopItemViewModel> loops = [];

    public IReadOnlyList<ILoopItemViewModel> Loops => loops;

    [ObservableProperty]
    public partial string Error { get; private set; } = string.Empty;

    public void Show(IReadOnlyList<LoopDigest> loops, TimeZoneInfo zone) =>
        this.loops.ShowOnly(loops.Select(digest => new LoopItemViewModel(digest, zone)));

    [RelayCommand(CanExecute = nameof(Pausable))]
    private Task PauseAsync(ILoopItemViewModel? loop, CancellationToken cancellationToken) =>
        ActAsync(controls.PauseAsync(loop!.Id, cancellationToken));

    [RelayCommand(CanExecute = nameof(Resumable))]
    private Task ResumeAsync(ILoopItemViewModel? loop, CancellationToken cancellationToken) =>
        ActAsync(controls.ResumeAsync(loop!.Id, cancellationToken));

    [RelayCommand(CanExecute = nameof(Stoppable))]
    private Task StopAsync(ILoopItemViewModel? loop, CancellationToken cancellationToken) =>
        ActAsync(controls.StopAsync(loop!.Id, cancellationToken));

    private static bool Pausable(ILoopItemViewModel? loop) => loop?.CanPause == true;

    private static bool Resumable(ILoopItemViewModel? loop) => loop?.CanResume == true;

    private static bool Stoppable(ILoopItemViewModel? loop) => loop?.CanStop == true;

    private async Task ActAsync(ValueTask<Result<LoopId, AutopilotError>> command) =>
        Error = (await command).Match(_ => string.Empty, LoopPhrases.Error);
}
