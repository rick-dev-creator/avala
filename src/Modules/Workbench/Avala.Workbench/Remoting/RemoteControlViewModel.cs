using Avala.Agents.Contracts.Sessions;
using Avala.Components.QrCodes;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Workbench.Board;
using Avala.Workbench.Following;
using Avala.Workbench.Linking;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Remoting;

internal interface IRemoteControlViewModel
{
    bool IsShown { get; }

    bool IsOn { get; }

    bool IsConnected { get; }

    bool IsOpen { get; set; }

    string Status { get; }

    string Link { get; }

    bool HasLink { get; }

    QrModules Code { get; }

    string Problem { get; }

    bool HasProblem { get; }

    IRelayCommand ToggleOpenCommand { get; }

    IAsyncRelayCommand ToggleCommand { get; }

    IAsyncRelayCommand CopyLinkCommand { get; }

    IAsyncRelayCommand OpenLinkCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class RemoteControlViewModel(JobBoard board, LiveFeed feed, IRemoteJobs jobs, LinkTools links)
    : IRemoteControlViewModel, IRegionAware<JobId>, IActivatable, IPresentation, IDisposable
{
    private Option<JobId> job;
    private Option<string> refused;

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
    public partial bool IsOn { get; private set; }

    [ObservableProperty]
    public partial bool IsConnected { get; private set; }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Link { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasLink { get; private set; }

    [ObservableProperty]
    public partial QrModules Code { get; private set; } = QrModules.None;

    [ObservableProperty]
    public partial string Problem { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasProblem { get; private set; }

    public void Activate() => feed.Start(_ => ValueTask.FromResult(job.Bind(found => board.Find(found).Map(shown => (Job: shown, Remote: board.RemoteOf(found))))), Show);

    public void Deactivate() => feed.Stop();

    public void Dispose() => feed.Dispose();

    public void OnRegionContextChanged(Option<JobId> context)
    {
        job = context;
        refused = Option<string>.None;
        IsOpen = false;
        feed.Refresh();
    }

    [RelayCommand]
    private void ToggleOpen() => IsOpen = !IsOpen;

    [RelayCommand]
    private async Task ToggleAsync(CancellationToken cancellationToken)
    {
        if (job.Match<JobId?>(found => found, () => null) is not { } shown)
        {
            return;
        }

        var result = await jobs.ControlRemotelyAsync(shown, !IsOn, cancellationToken);
        refused = result.Match(_ => Option<string>.None, rejection => RemotePhrases.Refusal(rejection));
        feed.Refresh();
    }

    [RelayCommand]
    private async Task CopyLinkAsync(CancellationToken cancellationToken) => _ = await links.Clipboard.CopyAsync(Link, cancellationToken);

    [RelayCommand]
    private async Task OpenLinkAsync(CancellationToken cancellationToken) => _ = await links.Links.OpenAsync(Link, cancellationToken);

    private void Show(Option<(BoardJob Job, BoardRemote Remote)> shown)
    {
        var found = shown.Match<BoardJob?>(pair => pair.Job, () => null);
        var kept = shown.Match(pair => pair.Remote, () => BoardRemote.None);
        var remote = kept.Control;
        var wanted = kept.Wanted.Match(on => on, () => found?.Summary.RemoteControlled ?? false);
        var link = remote.IsOn ? remote.Link.Match(value => value, () => string.Empty) : string.Empty;
        IsShown = found is not null && RemotePhrases.Offered(found, kept.Offered || wanted);
        IsOn = wanted;
        IsConnected = wanted && remote.State == RemoteState.Connected;
        Link = link;
        HasLink = link.Length > 0;
        Code = HasLink ? QrModules.Of(link) : QrModules.None;
        Status = found is null ? string.Empty : RemotePhrases.Status(wanted, found.Status, remote);
        Problem = refused.Match(text => text, () => remote.State == RemoteState.Failed ? remote.Problem.Match(text => text, () => RemotePhrases.Stopped) : string.Empty);
        HasProblem = Problem.Length > 0;
    }
}
