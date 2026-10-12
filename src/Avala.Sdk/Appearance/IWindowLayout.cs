namespace Avala.Sdk.Appearance;

public sealed record WindowLayout(bool IsSidebarCollapsed, double SidebarWidth)
{
    public const double DefaultSidebarWidth = 260;

    public const double NarrowestSidebar = 200;

    public const double WidestSidebar = 400;

    public const double DefaultPreviewShare = 0.45;

    public const double SmallestPreviewShare = 0.25;

    public const double LargestPreviewShare = 0.7;

    public static WindowLayout Default { get; } = new(false, DefaultSidebarWidth);

    public double PreviewShare { get; init; } = DefaultPreviewShare;

    public static double Fit(double sidebarWidth) =>
        double.IsFinite(sidebarWidth) ? Math.Round(Math.Clamp(sidebarWidth, NarrowestSidebar, WidestSidebar)) : DefaultSidebarWidth;

    public static double FitShare(double previewShare) =>
        double.IsFinite(previewShare) ? Math.Round(Math.Clamp(previewShare, SmallestPreviewShare, LargestPreviewShare), 3) : DefaultPreviewShare;
}

public interface IWindowLayout
{
    ValueTask<WindowLayout> ReadAsync(CancellationToken cancellationToken);

    ValueTask RememberAsync(Func<WindowLayout, WindowLayout> change, CancellationToken cancellationToken);
}
