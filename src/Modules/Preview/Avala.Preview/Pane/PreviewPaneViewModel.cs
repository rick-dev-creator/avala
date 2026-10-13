using Avala.Jobs.Contracts;
using Avala.Preview.Contracts;
using Avala.Preview.Navigation;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Preview.Pane;

internal interface IPreviewPaneViewModel
{
    bool IsOpen { get; }

    string Address { get; }

    string State { get; }

    bool IsLive { get; }

    bool AllowsNetwork { get; }

    IWebSurface? Surface { get; }

    bool IsEmbedded { get; }

    string Notice { get; }

    bool HasRefusal { get; }

    string Refusal { get; }

    IRelayCommand BackCommand { get; }

    IRelayCommand ReloadCommand { get; }

    IAsyncRelayCommand ToggleNetworkCommand { get; }

    IAsyncRelayCommand OpenInBrowserCommand { get; }

    IAsyncRelayCommand OpenRefusedCommand { get; }

    IRelayCommand DismissRefusalCommand { get; }

    IRelayCommand CloseCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class PreviewPaneViewModel
    : IPreviewPaneViewModel, IPane, IRegionAware<JobId>, IActivatable, IPresentation, IRecipient<PreviewRequested>, IRecipient<LocalPageRequested>, IDisposable
{
    private readonly PreviewDesk desk;
    private readonly IWebSurfaces surfaces;
    private readonly ILinkOpener links;
    private readonly IMessenger messenger;
    private readonly HashSet<JobId> networked = [];
    private readonly HashSet<PreviewTarget> seen = [];
    private Option<JobId> job;
    private Option<PreviewTarget> shown;
    private Option<Uri> page;
    private Option<Uri> refused;
    private bool starting;

    public PreviewPaneViewModel(PreviewDesk desk, IWebSurfaces surfaces, ILinkOpener links, IMessenger messenger)
    {
        this.desk = desk;
        this.surfaces = surfaces;
        this.links = links;
        this.messenger = messenger;
        messenger.RegisterAll(this);
    }

    public event EventHandler<Presented>? Presented;

    public long Revision { get; private set; }

    public Task Following => desk.Feed.Following;

    public Task Navigating { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    public partial string Address { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Notice))]
    public partial string State { get; private set; } = PreviewPhrases.Stopped;

    [ObservableProperty]
    public partial bool IsLive { get; private set; }

    [ObservableProperty]
    public partial bool AllowsNetwork { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmbedded), nameof(Notice))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand), nameof(ReloadCommand), nameof(OpenInBrowserCommand))]
    public partial IWebSurface? Surface { get; private set; }

    public bool IsEmbedded => Surface?.Embedding == SurfaceEmbedding.Embedded;

    public string Notice =>
        Surface is { } surface ? PreviewPhrases.Notice(surface.Embedding) : State == PreviewPhrases.Starting ? PreviewPhrases.Waiting : string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRefusal))]
    public partial string Refusal { get; private set; } = string.Empty;

    public bool HasRefusal => Refusal.Length > 0;

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
        if (context == job)
        {
            return;
        }

        Detach();
        job = context;
        starting = false;
        _ = job.Bind(desk.TargetOf).Map(seen.Add);
        AllowsNetwork = job.Match(networked.Contains, () => false);
        ToggleNetworkCommand.NotifyCanExecuteChanged();
        Refresh();
    }

    public void Receive(PreviewRequested message)
    {
        if (job != Option<JobId>.Some(message.Job))
        {
            return;
        }

        starting = desk.TargetOf(message.Job).IsNone;
        Open(true);
        Refresh();
    }

    public void Receive(LocalPageRequested message)
    {
        if (!job.Match(current => desk.Serves(current, message.Page), () => false))
        {
            return;
        }

        message.Show();
        page = message.Page;
        shown = Option<PreviewTarget>.None;
        starting = job.Bind(desk.TargetOf).IsNone;
        Open(true);
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => Surface?.GoBack();

    [RelayCommand(CanExecute = nameof(HasSurface))]
    private void Reload() => Surface?.Reload();

    [RelayCommand(CanExecute = nameof(HasJob))]
    private Task ToggleNetworkAsync(CancellationToken cancellationToken) =>
        job.Match(current => SwitchNetworkAsync(current, cancellationToken), () => Task.CompletedTask);

    [RelayCommand(CanExecute = nameof(HasAddress))]
    private async Task OpenInBrowserAsync(CancellationToken cancellationToken) =>
        await shown.Match(
            target => links.OpenAsync(Outside(target), cancellationToken).AsTask(),
            () => Task.FromResult(Result<Uri, FileOpenError>.Failure(FileOpenError.Unavailable)));

    [RelayCommand]
    private async Task OpenRefusedAsync(CancellationToken cancellationToken)
    {
        var asked = refused;
        DismissRefusal();
        await asked.Match(address => links.OpenAsync(address, cancellationToken).AsTask(), () => Task.FromResult(Result<Uri, FileOpenError>.Failure(FileOpenError.Unavailable)));
    }

    [RelayCommand]
    private void DismissRefusal()
    {
        refused = Option<Uri>.None;
        Refusal = string.Empty;
    }

    [RelayCommand]
    private void Close()
    {
        starting = false;
        Detach();
        Open(false);
        Present();
    }

    private async Task SwitchNetworkAsync(JobId current, CancellationToken cancellationToken)
    {
        AllowsNetwork = !AllowsNetwork;
        _ = AllowsNetwork ? networked.Add(current) : networked.Remove(current);
        await desk.AllowNetworkAsync(current, AllowsNetwork, cancellationToken);
        Surface?.Reload();
    }

    private Uri Outside(PreviewTarget target) =>
        (Surface?.Address ?? Option<Uri>.None).Match(
            page => page.IsAbsoluteUri && page.Port != target.Address.Port && page.Scheme is "http" or "https" ? new Uri(target.Address, page.PathAndQuery) : page,
            () => target.Address);

    private bool CanGoBack() => Surface?.CanGoBack == true;

    private bool HasSurface() => Surface is not null;

    private bool HasJob() => job.IsSome;

    private bool HasAddress() => shown.IsSome;

    private void Refresh()
    {
        var target = job.Bind(desk.TargetOf);
        starting = starting && target.IsNone;

        if (target.Match(seen.Add, () => false))
        {
            Open(true);
        }

        if (IsOpen)
        {
            _ = target.Match(Show, () => Idle());
        }
        else if (job.IsNone)
        {
            Open(false);
        }

        Present();
    }

    private bool Show(PreviewTarget target)
    {
        Address = target.Origin;
        State = PreviewPhrases.Running;
        IsLive = true;

        if (shown != Option<PreviewTarget>.Some(target) || Surface is null)
        {
            var surface = Surface ?? Attach(target.Job);
            shown = target;
            OpenInBrowserCommand.NotifyCanExecuteChanged();
            Navigating = NavigateAsync(surface, target);
        }

        return true;
    }

    private bool Idle()
    {
        IsLive = false;
        State = starting ? PreviewPhrases.Starting : PreviewPhrases.Stopped;

        if (job.IsNone || (!starting && shown.IsNone))
        {
            Detach();
            Open(false);
        }

        return false;
    }

    private async Task NavigateAsync(IWebSurface surface, PreviewTarget target)
    {
        var asked = page;
        page = Option<Uri>.None;
        var gateway = await desk.GatewayAsync(target, CancellationToken.None);

        if (Surface == surface && shown == Option<PreviewTarget>.Some(target))
        {
            var root = gateway.Match(address => address, _ => target.Address);
            surface.Navigate(asked.Match(link => link.Port == target.Address.Port ? new Uri(root, link.PathAndQuery) : link, () => root));
            Present();
        }
    }

    private IWebSurface Attach(JobId owner)
    {
        var surface = surfaces.Open(desk.Rules(owner, () => AllowsNetwork));
        surface.Refused += OnRefused;
        surface.Changed += OnChanged;
        Surface = surface;

        return surface;
    }

    private void Detach()
    {
        if (Surface is { } surface)
        {
            surface.Refused -= OnRefused;
            surface.Changed -= OnChanged;
            surface.Close();
        }

        Surface = null;
        shown = Option<PreviewTarget>.None;
        Address = string.Empty;
        IsLive = false;
        DismissRefusal();
        OpenInBrowserCommand.NotifyCanExecuteChanged();
    }

    private void Open(bool open)
    {
        if (IsOpen != open)
        {
            IsOpen = open;
            _ = job.Match(
                current =>
                {
                    messenger.Send(new PreviewPaneChanged(current, open));

                    return true;
                },
                () => false);
        }
    }

    private void OnRefused(object? sender, Uri address)
    {
        refused = address;
        Refusal = PreviewPhrases.Refusal(address);
        Present();
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        BackCommand.NotifyCanExecuteChanged();
        Present();
    }

    private void Present()
    {
        Revision++;
        Presented?.Invoke(this, new Presented(Revision));
    }
}
