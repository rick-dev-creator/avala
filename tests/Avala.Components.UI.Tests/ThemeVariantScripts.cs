using Avala.Testing.UI;
using Avalonia.Media;
using Avalonia.Styling;

namespace Avala.Components.UI.Tests;

public sealed class ThemeVariantScripts(HeadlessUi ui)
{
    private static readonly string[] Surfaces = ["SurfaceWindowBrush", "SurfacePanelBrush", "SurfaceFloatBrush", "SurfaceCanvasBrush"];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string> Variants => ["Dark", "Light"];

    [Fact]
    public void ContrastMatchesTheWcagReferenceValues()
    {
        Assert.Equal(21, Legibility.Contrast(Colors.White, Colors.Black), 2);
        Assert.Equal(4.48, Legibility.Contrast(Color.Parse("#777777"), Colors.White), 2);
        Assert.Equal(4.54, Legibility.Contrast(Color.Parse("#767676"), Colors.White), 2);
        Assert.Equal(1, Legibility.Contrast(Color.Parse("#336699"), Color.Parse("#336699")), 2);
        Assert.Equal(Color.Parse("#808080"), Legibility.Over(Color.Parse("#80FFFFFF"), Colors.Black));
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public Task EveryTextLevelIsLegibleOnEverySurfaceAsync(string name) =>
        ui.RunAsync(() =>
        {
            var variant = name == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

            foreach (var surface in Surfaces.Select(key => Legibility.Resource(key, variant)))
            {
                Assert.True(Legibility.Contrast(Legibility.Resource("TextPrimaryBrush", variant), surface) >= 7, $"primary on {surface}");
                Assert.True(Legibility.Contrast(Legibility.Resource("TextSecondaryBrush", variant), surface) >= 4.5, $"secondary on {surface}");
                Assert.True(Legibility.Contrast(Legibility.Resource("TextTertiaryBrush", variant), surface) >= 3, $"tertiary on {surface}");
                Assert.True(Legibility.Contrast(Legibility.Resource("AttentionBrush", variant), surface) >= 3, $"attention on {surface}");
                Assert.True(Legibility.Contrast(Legibility.Resource("FailureBrush", variant), surface) >= 3, $"failure on {surface}");
            }

            Assert.True(Legibility.Contrast(Legibility.Resource("AccentBrush", variant), Legibility.Resource("SurfaceWindowBrush", variant)) >= 4.5);
            Assert.True(Legibility.Contrast(Legibility.Resource("OnAccentBrush", variant), Legibility.Resource("AccentBrush", variant)) >= 4.5);
        }, Cancellation);

    [Fact]
    public Task TheTwoVariantsDifferInEveryNeutralSurfaceAsync() =>
        ui.RunAsync(() =>
            Assert.All(Surfaces, key => Assert.NotEqual(Legibility.Resource(key, ThemeVariant.Dark), Legibility.Resource(key, ThemeVariant.Light))), Cancellation);
}
