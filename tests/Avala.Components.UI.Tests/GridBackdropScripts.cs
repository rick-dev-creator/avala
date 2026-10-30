using Avala.Components.UI.Theme;

namespace Avala.Components.UI.Tests;

public sealed class GridBackdropScripts
{
    [Fact]
    public void TheGridDrawsALineEverySpacingOnTheHalfPixel() =>
        Assert.Equal([47.5, 95.5], GridBackdrop.Lines(100, 48));

    [Fact]
    public void AGridWithoutSpacingDrawsNothing() =>
        Assert.Empty(GridBackdrop.Lines(100, 0));
}
