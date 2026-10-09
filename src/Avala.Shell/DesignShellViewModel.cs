using Avala.Components.Keycaps;
using Avala.Components.Meters;
using Avala.Components.Status;
using Avala.Sdk;
using Avala.Sdk.Regions;
using Avala.Shell.Regions;

namespace Avala.Shell;

internal sealed class DesignShellViewModel : IShellViewModel
{
    public Region Toolbar { get; } = new(ShellRegions.Toolbar, [new KeycapHintViewModel("⌘K", "jump")]);

    public Region Sidebar { get; } = new(
        ShellRegions.Sidebar,
        [
            new StatusPillViewModel(StatusKind.NeedsYou, "Add invoice PDF endpoint"),
            new StatusPillViewModel(StatusKind.Working, "Fix JPY rounding in invoice totals"),
            new StatusPillViewModel(StatusKind.Held, "Extract sync queue into a module"),
            new StatusPillViewModel(StatusKind.ReadyForReview, "Rate-limit POST /login"),
        ]);

    public Region SidebarFooter { get; } = new(ShellRegions.SidebarFooter, [Usage("claude-work · 5h", 0.88, "resets 16:20")]);

    public Region Content { get; } = new(ShellRegions.Content, []);

    public Region Inspector { get; } = new(ShellRegions.Inspector, [Usage("claude-personal · 5h", 0.31, "resets 17:05")]);

    public IReadOnlyList<IPage> Pages { get; } =
    [
        new DesignPage("Overview", "IconOverview"),
        new DesignPage("Usage", "IconUsage"),
        new DesignPage("Settings", "IconSettings"),
    ];

    public IReadOnlyList<IPage> NavigationPages => Pages;

    public IPage? SelectedPage
    {
        get => null;
        set { }
    }

    public bool HasNavigation => true;

    public bool HasSidebar => true;

    public bool IsInspectorShown => true;

    private sealed record DesignPage(string Title, string Icon) : IPage;

    private static MeterViewModel Usage(string label, double fraction, string detail)
    {
        var meter = new MeterViewModel(label, 0.9);
        meter.Show(fraction, detail);

        return meter;
    }
}
