using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Shell.Regions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Shell;

[INotifyPropertyChanged]
internal sealed partial class ShellViewModel : IShellViewModel, IActivatable, IPresentation
{
    private bool active;

    public ShellViewModel(IEnumerable<IPage> pages, IEnumerable<RegionContribution> contributions, RegionContexts contexts)
    {
        var contributed = contributions.ToList();
        Toolbar = Region.Of(ShellRegions.Toolbar, contributed);
        Sidebar = Region.Of(ShellRegions.Sidebar, contributed);
        SidebarFooter = Region.Of(ShellRegions.SidebarFooter, contributed);
        Inspector = Region.Of(ShellRegions.Inspector, contributed);
        Content = new Region(ShellRegions.Content, [.. pages, .. Region.Of(ShellRegions.Content, contributed).Items.Select(AsPage)]);
        Pages = [.. Content.Items.Cast<IPage>()];
        SelectedPage = Pages.Count > 0 ? Pages[0] : null;

        foreach (var region in Regions)
        {
            contexts.Attach(region);
        }

        contexts.Delivered += _ => Present();
    }

    public event EventHandler<Presented>? Presented;

    public long Revision { get; private set; }

    public Region Toolbar { get; }

    public Region Sidebar { get; }

    public Region SidebarFooter { get; }

    public Region Content { get; }

    public Region Inspector { get; }

    public IReadOnlyList<IPage> Pages { get; }

    [ObservableProperty]
    public partial IPage? SelectedPage { get; set; }

    public bool HasNavigation => Pages.Count > 1;

    public bool HasSidebar => HasNavigation || Toolbar.HasItems || Sidebar.HasItems || SidebarFooter.HasItems;

    private IEnumerable<Region> Regions => [Toolbar, Sidebar, SidebarFooter, Content, Inspector];

    public void Activate()
    {
        if (active)
        {
            return;
        }

        active = true;
        Toolbar.Activate();
        Sidebar.Activate();
        SidebarFooter.Activate();
        Inspector.Activate();
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
        Inspector.Deactivate();
        SidebarFooter.Deactivate();
        Sidebar.Deactivate();
        Toolbar.Deactivate();
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

    private void Present()
    {
        Revision++;
        Presented?.Invoke(this, new Presented(Revision));
    }

    private static IPage AsPage(object contribution) =>
        contribution as IPage
        ?? throw new InvalidOperationException($"{contribution.GetType().Name} is registered into the content region but is not a page.");
}
