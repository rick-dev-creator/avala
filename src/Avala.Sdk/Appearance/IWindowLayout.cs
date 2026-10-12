namespace Avala.Sdk.Appearance;

public sealed record WindowLayout(bool IsSidebarCollapsed, double SidebarWidth)
{
    public const double DefaultSidebarWidth = 260;

    public const double NarrowestSidebar = 200;

    public const double WidestSidebar = 400;

    public static WindowLayout Default { get; } = new(false, DefaultSidebarWidth);

    public static double Fit(double sidebarWidth) =>
        double.IsFinite(sidebarWidth) ? Math.Round(Math.Clamp(sidebarWidth, NarrowestSidebar, WidestSidebar)) : DefaultSidebarWidth;
}

public interface IWindowLayout
{
    ValueTask<WindowLayout> ReadAsync(CancellationToken cancellationToken);

    ValueTask RememberAsync(WindowLayout layout, CancellationToken cancellationToken);
}
