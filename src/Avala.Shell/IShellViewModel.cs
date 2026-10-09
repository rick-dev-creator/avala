using Avala.Sdk;
using Avala.Shell.Regions;

namespace Avala.Shell;

internal interface IShellViewModel
{
    Region Toolbar { get; }

    Region Sidebar { get; }

    Region SidebarFooter { get; }

    Region Content { get; }

    Region Inspector { get; }

    IReadOnlyList<IPage> Pages { get; }

    IPage? SelectedPage { get; set; }

    bool HasNavigation { get; }

    bool HasSidebar { get; }

    bool IsInspectorShown { get; }
}
