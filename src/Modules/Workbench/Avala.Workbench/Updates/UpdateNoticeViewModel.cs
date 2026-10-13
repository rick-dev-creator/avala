using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Updates;
using Avala.Workbench.Following;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Updates;

internal interface IUpdateNoticeViewModel
{
    bool IsShown { get; }

    string Text { get; }

    string Hint { get; }

    string Note { get; }

    IAsyncRelayCommand OpenCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class UpdateNoticeViewModel(IUpdates updates, LiveFeed feed, ILinkOpener links)
    : IUpdateNoticeViewModel, IActivatable, IPresentation, IDisposable
{
    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    public long Revision => feed.Revision;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown), nameof(Text), nameof(Hint))]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    public partial UpdateState State { get; private set; } = UpdateState.NotChecked;

    [ObservableProperty]
    public partial string Note { get; private set; } = string.Empty;

    public bool IsShown => State.Update.IsSome;

    public string Text => UpdatePhrases.Notice(State);

    public string Hint => UpdatePhrases.Hint(State);

    public void Activate() => feed.Start(_ => ValueTask.FromResult(updates.Latest), Show);

    public void Deactivate() => feed.Stop();

    public void Dispose() => feed.Dispose();

    private void Show(UpdateState state) => State = state;

    private bool CanOpen() => IsShown && State.Status != UpdateStatus.Downloading;

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private async Task OpenAsync(CancellationToken cancellationToken)
    {
        if (State.CanRestart)
        {
            State = await updates.RestartAsync(cancellationToken);
        }
        else if (State.CanDownload)
        {
            State = State with { Status = UpdateStatus.Downloading };
            State = await updates.DownloadAsync(cancellationToken);
            Note = State.Status == UpdateStatus.DownloadFailed ? UpdatePhrases.DownloadFailed : string.Empty;
        }
        else if (State.Update.Match<AvailableUpdate?>(update => update, () => null) is { } update)
        {
            Note = (await links.OpenAsync(update.Release, cancellationToken)).Match(_ => string.Empty, _ => UpdatePhrases.Unopened(update.Release));
        }
    }
}
