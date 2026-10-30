using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Workbench.Following;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Overview;

internal interface IRelaysToggleViewModel
{
    bool IsShown { get; }

    bool IsOpen { get; }

    IRelayCommand ToggleCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class RelaysToggleViewModel
    : IRelaysToggleViewModel, IRegionAware<JobId>, IActivatable, IPresentation, IRecipient<RelaysPaneChanged>, IDisposable
{
    private readonly RelaysReader reader;
    private readonly LiveFeed feed;
    private readonly IMessenger messenger;
    private Option<JobId> job;

    public RelaysToggleViewModel(RelaysReader reader, LiveFeed feed, IMessenger messenger)
    {
        this.reader = reader;
        this.feed = feed;
        this.messenger = messenger;
        messenger.RegisterAll(this);
    }

    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    public long Revision => feed.Revision;

    public Task Following => feed.Following;

    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    public void Activate() => feed.Start(_ => ValueTask.FromResult(job.Match(Shows, () => false)), shown => IsShown = shown);

    public void Deactivate() => feed.Stop();

    public void Dispose()
    {
        feed.Dispose();
        messenger.UnregisterAll(this);
    }

    public void OnRegionContextChanged(Option<JobId> context)
    {
        job = context;
        IsOpen = false;
        feed.Refresh();
    }

    public void Receive(RelaysPaneChanged message)
    {
        if (job == Option<JobId>.Some(message.Job))
        {
            IsOpen = message.IsOpen;
        }
    }

    [RelayCommand]
    private void Toggle()
    {
        foreach (var current in job.Match<JobId[]>(found => [found], () => []))
        {
            messenger.Send(new RelaysPaneRequested(current));
        }
    }

    private bool Shows(JobId shown) => reader.Offers(shown);
}
