using Avala.Jobs.Contracts;
using Avala.Preview.Navigation;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Preview.Pane;

internal enum ChipPlacement
{
    Header,
    Conversation,
}

internal interface IRunningChipViewModel
{
    bool IsShown { get; }

    string Text { get; }

    string Lease { get; }

    bool IsUnleased { get; }

    IRelayCommand OpenCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class RunningChipViewModel
    : IRunningChipViewModel, IRegionAware<JobId>, IActivatable, IPresentation, IRecipient<PreviewPaneChanged>, IDisposable
{
    private readonly PreviewDesk desk;
    private readonly IMessenger messenger;
    private readonly ChipPlacement placement;
    private Option<JobId> job;
    private bool paneOpen;

    public RunningChipViewModel(PreviewDesk desk, IMessenger messenger, ChipPlacement placement)
    {
        this.desk = desk;
        this.messenger = messenger;
        this.placement = placement;
        messenger.Register(this);
    }

    public event EventHandler<Presented>? Presented;

    public long Revision { get; private set; }

    public Task Following => desk.Feed.Following;

    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    [ObservableProperty]
    public partial string Text { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnleased))]
    public partial string Lease { get; private set; } = string.Empty;

    public bool IsUnleased => Lease.Length > 0;

    public void Activate()
    {
        desk.Feed.Start(Refresh);
        Refresh();
    }

    public void Deactivate() => desk.Feed.Stop();

    public void Dispose()
    {
        desk.Feed.Dispose();
        messenger.UnregisterAll(this);
    }

    public void OnRegionContextChanged(Option<JobId> context)
    {
        job = context;
        paneOpen = false;
        Refresh();
    }

    public void Receive(PreviewPaneChanged message)
    {
        if (job == Option<JobId>.Some(message.Job))
        {
            paneOpen = message.IsOpen;
            Refresh();
        }
    }

    [RelayCommand(CanExecute = nameof(IsShown))]
    private void Open() => _ = job.Match(
        current =>
        {
            messenger.Send(new PreviewRequested(current));

            return true;
        },
        () => false);

    private void Refresh()
    {
        var target = job.Bind(desk.TargetOf);
        Text = target.Match(PreviewPhrases.Chip, () => string.Empty);
        Lease = target.Match(PreviewPhrases.Lease, () => string.Empty);
        IsShown = target.IsSome && !(placement == ChipPlacement.Conversation && paneOpen);
        OpenCommand.NotifyCanExecuteChanged();
        Revision++;
        Presented?.Invoke(this, new Presented(Revision));
    }
}
