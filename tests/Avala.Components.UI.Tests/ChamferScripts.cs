using Avala.Components.UI.Theme;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Avala.Components.UI.Tests;

public sealed class ChamferScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task APanelWithAChamferCutsItsTopRightAndBottomLeftCornersAsync() =>
        ui.RunAsync(() =>
        {
            var (_, panel) = Shown(new Border { Width = 200, Height = 80 }, cut: 12);

            Assert.Null(panel.Clip);
            Assert.Equal(new RelativeRect(0, 0, 200, 80, RelativeUnit.Absolute), Assert.IsType<DrawingBrush>(panel.OpacityMask).DestinationRect);
            Assert.Equal([new(0, 0), new(188, 0), new(200, 12), new(200, 80), new(12, 80), new(0, 68)], Outline(panel).Points);
        }, Cancellation);

    [Fact]
    public Task AShadowedPanelKeepsItsWholeSizeUnderTheCutAsync() =>
        ui.RunAsync(() =>
        {
            var (_, panel) = Shown(new Border { Width = 200, Height = 80, BoxShadow = BoxShadows.Parse("0 24 48 -12 #80000000") }, cut: 12);

            Assert.Equal(new RelativeRect(0, 0, 200, 80, RelativeUnit.Absolute), Assert.IsType<DrawingBrush>(panel.OpacityMask).DestinationRect);
        }, Cancellation);

    [Fact]
    public Task TheCutFollowsThePanelWhenItGrowsAsync() =>
        ui.RunAsync(() =>
        {
            var (view, panel) = Shown(new Border { Width = 200, Height = 80 }, cut: 12);

            panel.Width = 400;
            view.Settle();

            Assert.Equal([new(0, 0), new(388, 0), new(400, 12), new(400, 80), new(12, 80), new(0, 68)], Outline(panel).Points);
        }, Cancellation);

    [Fact]
    public Task RemovingTheChamferOrAPanelTooSmallToCutLeavesItUnclippedAsync() =>
        ui.RunAsync(() =>
        {
            var (_, removed) = Shown(new Border { Width = 200, Height = 80 }, cut: 12);
            var (_, tiny) = Shown(new Border { Width = 16, Height = 16 }, cut: 12);

            Chamfer.SetSize(removed, 0);

            Assert.Equal((null, null), (removed.OpacityMask, tiny.OpacityMask));
        }, Cancellation);

    private static PolylineGeometry Outline(Border panel) =>
        Assert.IsType<PolylineGeometry>(Assert.IsType<GeometryDrawing>(Assert.IsType<DrawingBrush>(panel.OpacityMask).Drawing).Geometry);

    private static (ViewScript View, Border Panel) Shown(Border panel, double cut)
    {
        Chamfer.SetSize(panel, cut);
        var view = ViewScript.Show(new ContentControl { Content = panel }, new object());
        return (view, panel);
    }
}
