using Avala.Components.UI.Theme;
using Avala.Sdk.Appearance;

namespace Avala.Components.UI.Tests;

public sealed class ThemesTests
{
    [Theory]
    [InlineData(ThemeChoice.Dark, true)]
    [InlineData(ThemeChoice.Tide, true)]
    [InlineData(ThemeChoice.Unit, true)]
    [InlineData(ThemeChoice.Ash, true)]
    [InlineData(ThemeChoice.Signal, true)]
    [InlineData(ThemeChoice.Light, false)]
    [InlineData(ThemeChoice.Bloom, false)]
    public void ANamedThemeIsDarkWhenItsBaseIsSoCodeIsColoredForIt(ThemeChoice theme, bool dark) =>
        Assert.Equal(dark, Themes.IsDark(Themes.Of(theme)));
}
