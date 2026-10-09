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
    [NotifyPropertyChangedFor(nameof(IsShown), nameof(Text))]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    public partial Option<AvailableUpdate> Update { get; private set; }

    [ObservableProperty]
    public partial string Note { get; private set; } = string.Empty;

    public bool IsShown => Update.IsSome;

    public string Text => Update.Match(update => UpdatePhrases.Available(update.Version), () => string.Empty);

    public void Activate() => feed.Start(_ => ValueTask.FromResult(updates.Latest), Show);

    public void Deactivate() => feed.Stop();

    public void Dispose() => feed.Dispose();

    private void Show(UpdateState state) => Update = state.Update;

    [RelayCommand(CanExecute = nameof(IsShown))]
    private async Task OpenAsync(CancellationToken cancellationToken)
    {
        if (Update.Match<AvailableUpdate?>(update => update, () => null) is { } update)
        {
            Note = (await links.OpenAsync(update.Release, cancellationToken)).Match(_ => string.Empty, _ => UpdatePhrases.Unopened(update.Release));
        }
    }
}
