using Avala.Components.UI.Theme;
using Avala.Sdk.Appearance;
using Avala.Testing.UI;
using Avalonia.Media;
using Avalonia.Styling;

namespace Avala.Components.UI.Tests;

public sealed class ThemeVariantScripts(HeadlessUi ui)
{
    private static readonly string[] Surfaces = ["SurfaceWindowBrush", "SurfacePanelBrush", "SurfaceFloatBrush", "SurfaceCanvasBrush"];

    private static ThemeVariant Variant(string name) => Themes.Of(Enum.Parse<ThemeChoice>(name));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string> Variants => ["Dark", "Light", "Tide", "Unit", "Ash", "Bloom", "Signal"];

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
            var variant = Variant(name);

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

    [Theory]
    [MemberData(nameof(Variants))]
    public Task AttentionCaptionsReadAsBodyTextOnEveryPanelAndOnAnOpenFoldOverItAsync(string name) =>
        ui.RunAsync(() =>
        {
            var variant = Variant(name);
            var attention = Legibility.Resource("AttentionBrush", variant);
            Color[] fills =
            [
                .. Surfaces.Select(key => Legibility.Resource(key, variant)),
                .. Surfaces.Select(key => Legibility.Over(Legibility.Resource("FillQuietBrush", variant), Legibility.Resource(key, variant))),
            ];

            Assert.All(fills, fill => Assert.True(Legibility.Contrast(attention, fill) >= 4.5, $"attention {attention} reads {Legibility.Contrast(attention, fill):0.00}:1 on {fill}"));
        }, Cancellation);

    [Theory]
    [InlineData("FailureBrush")]
    [InlineData("AttentionBrush")]
    public Task TheLightThemeKeepsTheHueAndVividnessOfTheDarkThemesStatusColourAsync(string key) =>
        ui.RunAsync(() =>
        {
            var dark = Legibility.Resource(key, ThemeVariant.Dark).ToHsl();
            var light = Legibility.Resource(key, ThemeVariant.Light).ToHsl();
            var hues = Math.Abs(dark.H - light.H);

            Assert.True(Math.Min(hues, 360 - hues) <= 10, $"hue {light.H} against {dark.H}");
            Assert.True(light.S >= dark.S - 0.1, $"saturation {light.S} against {dark.S}");
        }, Cancellation);

    [Theory]
    [MemberData(nameof(Variants))]
    public Task GraphNodesHideTheEdgesBehindThemAsync(string name) =>
        ui.RunAsync(() => Assert.Equal(255, Legibility.Resource("NodeBrush", Variant(name)).A), Cancellation);

    [Theory]
    [MemberData(nameof(Variants))]
    public Task QuietEdgesAndLimitTracksStandOutFromTheWindowBelowFlowingEdgesAsync(string name) =>
        ui.RunAsync(() =>
        {
            var variant = Variant(name);
            var window = Legibility.Resource("SurfaceWindowBrush", variant);
            double Against(string key) => Legibility.Contrast(Legibility.Over(Legibility.Resource(key, variant), window), window);

            Assert.InRange(Against("EdgeQuietBrush"), 1.75, Against("EdgeFlowingBrush"));
            Assert.True(Against("HubTrackBrush") >= 1.5, $"track at {Against("HubTrackBrush")}");
        }, Cancellation);

    [Theory]
    [InlineData("EdgeQuietBrush")]
    [InlineData("EdgeFlowingBrush")]
    public Task AnEdgeStandsOutFromTheLightWindowAtLeastAsMuchAsFromTheDarkOneAsync(string key) =>
        ui.RunAsync(() =>
        {
            double Against(ThemeVariant variant) =>
                Legibility.Contrast(Legibility.Over(Legibility.Resource(key, variant), Legibility.Resource("SurfaceWindowBrush", variant)), Legibility.Resource("SurfaceWindowBrush", variant));

            Assert.True(Against(ThemeVariant.Light) >= Against(ThemeVariant.Dark), $"{Against(ThemeVariant.Light):0.00}:1 in light against {Against(ThemeVariant.Dark):0.00}:1 in dark");
        }, Cancellation);

    [Fact]
    public Task TheTwoVariantsDifferInEveryNeutralSurfaceAsync() =>
        ui.RunAsync(() =>
            Assert.All(Surfaces, key => Assert.NotEqual(Legibility.Resource(key, ThemeVariant.Dark), Legibility.Resource(key, ThemeVariant.Light))), Cancellation);
}
