using Avala.Testing.UI;
using Avalonia;
using Avalonia.Media;

namespace Avala.Components.UI.Tests;

public sealed class TypographyScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("HudFont", "Chakra Petch", FontWeight.SemiBold)]
    [InlineData("HudFont", "Chakra Petch", FontWeight.Medium)]
    [InlineData("VoiceFont", "Zen Old Mincho", FontWeight.Bold)]
    public Task TheLabelAndVoiceFontsAreEmbeddedInTheirWeightsAsync(string key, string family, FontWeight weight) =>
        ui.RunAsync(() =>
        {
            Assert.True(Application.Current!.TryGetResource(key, null, out var found));
            var typeface = new Typeface((FontFamily)found!, FontStyle.Normal, weight);

            Assert.True(FontManager.Current.TryGetGlyphTypeface(typeface, out var glyphs));
            Assert.Equal((true, weight), (glyphs.FamilyName.StartsWith(family, StringComparison.Ordinal), glyphs.Weight));
        }, Cancellation);

    [Fact]
    public Task AvasVoiceDrawsThePanelTitlesInJapaneseAsync() =>
        ui.RunAsync(() =>
        {
            Assert.True(Application.Current!.TryGetResource("VoiceFont", null, out var found));
            Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface((FontFamily)found!, FontStyle.Normal, FontWeight.Bold), out var glyphs));

            Assert.All("メニュー記録目標状態", character => Assert.True(glyphs.CharacterToGlyphMap.ContainsGlyph(character), $"{character} has no glyph"));
        }, Cancellation);
}
