using Avala.Sdk;
using Avala.Sdk.Appearance;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Shell.Regions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Shell;

[INotifyPropertyChanged]
internal sealed partial class ShellViewModel : IShellViewModel, IActivatable, IPresentation, IRecipient<PageRequested>
{
    public const double NarrowWidth = 1000;

    private readonly RegionContexts contexts;
    private readonly IWindowLayout layout;
    private bool active;
    private bool restored;

    public ShellViewModel(IEnumerable<IPage> pages, IEnumerable<RegionContribution> contributions, RegionContexts contexts, IWindowLayout layout)
    {
        this.contexts = contexts;
        this.layout = layout;
        var contributed = contributions.ToList();
        Toolbar = Region.Of(ShellRegions.Toolbar, contributed);
        Sidebar = Region.Of(ShellRegions.Sidebar, contributed);
        SidebarFooter = Region.Of(ShellRegions.SidebarFooter, contributed);
        Inspector = Region.Of(ShellRegions.Inspector, contributed);
        Overlay = Region.Of(ShellRegions.Overlay, contributed);
        Content = new Region(ShellRegions.Content, [.. pages, .. Region.Of(ShellRegions.Content, contributed).Items.Select(AsPage)]);
        Pages = [.. Content.Items.Cast<IPage>()];
        NavigationPages = [.. Pages.Where(page => page.Placement == PagePlacement.Navigation)];
        SelectedPage = Pages.Count > 0 ? Pages[0] : null;

        foreach (var region in Regions)
        {
            contexts.Attach(region);
        }

        contexts.Delivered += OnDelivered;
    }

    public event EventHandler<Presented>? Presented;

    public long Revision { get; private set; }

    public Region Toolbar { get; }

    public Region Sidebar { get; }

    public Region SidebarFooter { get; }

    public Region Content { get; }

    public Region Inspector { get; }

    public Region Overlay { get; }

    public IReadOnlyList<IPage> Pages { get; }

    public IReadOnlyList<IPage> NavigationPages { get; }

    [ObservableProperty]
    public partial IPage? SelectedPage { get; set; }

    public bool HasNavigation => Pages.Count > 1 && NavigationPages.Count > 0;

    public bool HasSidebar => HasNavigation || Toolbar.HasItems || Sidebar.HasItems || SidebarFooter.HasItems;

    public bool IsInspectorShown => !IsFocused && Inspector.HasItems && contexts.ContextOf(ShellRegions.Inspector).IsSome;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarShown))]
    public partial bool IsSidebarCollapsed { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarShown))]
    public partial bool IsNarrow { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarShown))]
    public partial bool IsSidebarPeeking { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarShown), nameof(IsInspectorShown))]
    [NotifyCanExecuteChangedFor(nameof(LeaveFocusCommand))]
    public partial bool IsFocused { get; private set; }

    [ObservableProperty]
    public partial double SidebarWidth { get; private set; } = WindowLayout.DefaultSidebarWidth;

    public bool IsSidebarShown => HasSidebar && !IsFocused && (IsNarrow ? IsSidebarPeeking : !IsSidebarCollapsed);

    public Task Restoring { get; private set; } = Task.CompletedTask;

    public Task Remembering { get; private set; } = Task.CompletedTask;

    private IEnumerable<Region> Regions => [Toolbar, Sidebar, SidebarFooter, Content, Inspector, Overlay];

    public void Receive(PageRequested message)
    {
        if (Pages.Contains(message.Page))
        {
            SelectedPage = message.Page;
        }
    }

    public void Activate()
    {
        if (active)
        {
            return;
        }

        active = true;

        if (!restored)
        {
            restored = true;
            Restoring = RestoreAsync();
        }

        Toolbar.Activate();
        Sidebar.Activate();
        SidebarFooter.Activate();
        Inspector.Activate();
        Overlay.Activate();
        (SelectedPage as IActivatable)?.Activate();
    }

    public void Deactivate()
    {
        if (!active)
        {
            return;
        }

        active = false;
        (SelectedPage as IActivatable)?.Deactivate();
        Overlay.Deactivate();
        Inspector.Deactivate();
        SidebarFooter.Deactivate();
        Sidebar.Deactivate();
        Toolbar.Deactivate();
    }

    [RelayCommand(CanExecute = nameof(HasSidebar))]
    private void ToggleSidebar()
    {
        if (IsFocused)
        {
            IsFocused = false;

            if (IsSidebarShown)
            {
                return;
            }
        }

        if (IsNarrow)
        {
            IsSidebarPeeking = !IsSidebarPeeking;

            return;
        }

        IsSidebarCollapsed = !IsSidebarCollapsed;
        RememberSidebar();
    }

    [RelayCommand]
    private void ResizeSidebar(double width) => SidebarWidth = WindowLayout.Fit(width);

    [RelayCommand]
    private void RememberSidebar()
    {
        var (collapsed, width) = (IsSidebarCollapsed, SidebarWidth);
        Remembering = layout.RememberAsync(remembered => remembered with { IsSidebarCollapsed = collapsed, SidebarWidth = width }, CancellationToken.None).AsTask();
    }

    [RelayCommand]
    private void ResetSidebarWidth()
    {
        SidebarWidth = WindowLayout.DefaultSidebarWidth;
        RememberSidebar();
    }

    [RelayCommand]
    private void ToggleFocus() => IsFocused = !IsFocused;

    [RelayCommand(CanExecute = nameof(IsFocused))]
    private void LeaveFocus() => IsFocused = false;

    [RelayCommand]
    private void Fit(double width)
    {
        IsNarrow = width < NarrowWidth;
        IsSidebarPeeking = IsNarrow && IsSidebarPeeking;
    }

    private async Task RestoreAsync()
    {
        var remembered = await layout.ReadAsync(CancellationToken.None);
        IsSidebarCollapsed = remembered.IsSidebarCollapsed;
        SidebarWidth = WindowLayout.Fit(remembered.SidebarWidth);
    }

    partial void OnSelectedPageChanged(IPage? oldValue, IPage? newValue)
    {
        if (active)
        {
            (oldValue as IActivatable)?.Deactivate();
            (newValue as IActivatable)?.Activate();
        }

        Present();
    }

    private void OnDelivered(RegionName region)
    {
        if (region == ShellRegions.Inspector)
        {
            OnPropertyChanged(nameof(IsInspectorShown));
        }

        Present();
    }

    private void Present()
    {
        Revision++;
        Presented?.Invoke(this, new Presented(Revision));
    }

    private static IPage AsPage(object contribution) =>
        contribution as IPage
        ?? throw new InvalidOperationException($"{contribution.GetType().Name} is registered into the content region but is not a page.");
}
