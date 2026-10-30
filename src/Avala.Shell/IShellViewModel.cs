using Avala.Sdk;
using Avala.Shell.Regions;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Shell;

internal interface IShellViewModel
{
    Region Toolbar { get; }

    Region Sidebar { get; }

    Region SidebarFooter { get; }

    Region Content { get; }

    Region Inspector { get; }

    Region Overlay { get; }

    IReadOnlyList<IPage> Pages { get; }

    IReadOnlyList<IPage> NavigationPages { get; }

    IPage? SelectedPage { get; set; }

    bool HasNavigation { get; }

    bool HasSidebar { get; }

    bool IsInspectorShown { get; }

    bool IsSidebarShown { get; }

    bool IsNarrow { get; }

    bool IsDrawerOpen { get; }

    bool IsInspectorFloating { get; }

    bool IsInspectorDocked { get; }

    IRelayCommand DismissInspectorCommand { get; }

    bool IsFocused { get; }

    double SidebarWidth { get; }

    IRelayCommand ToggleSidebarCommand { get; }

    IRelayCommand ToggleFocusCommand { get; }

    IRelayCommand LeaveFocusCommand { get; }

    IRelayCommand<double> FitCommand { get; }

    IRelayCommand<double> ResizeSidebarCommand { get; }

    IRelayCommand RememberSidebarCommand { get; }

    IRelayCommand ResetSidebarWidthCommand { get; }
}
