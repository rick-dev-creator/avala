using Avala.Testing.UI;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Avala.Components.UI.Tests;

public sealed class FoldScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AFoldShowsItsHeaderAndFactAndKeepsItsContentFoldedAsync() =>
        ui.RunAsync(() =>
        {
            var view = ViewScript.Show(Fold(), new object());

            Assert.Contains("Usage & caps", view.VisibleTexts);
            Assert.Contains("USD 0.84 of USD 5", view.VisibleTexts);
            Assert.False(view.Shows("Body"));
        }, Cancellation);

    [Fact]
    public Task ClickingTheHeaderOpensTheFoldAndClickingAgainFoldsItAsync() =>
        ui.RunAsync(() =>
        {
            var fold = Fold();
            var view = ViewScript.Show(fold, new object());

            view.Click(Header(fold));
            var opened = (fold.IsExpanded, view.Shows("Body"), fold.Classes.Contains(":expanded"));
            view.Click(Header(fold));

            Assert.Equal((true, true, true), opened);
            Assert.Equal((false, false), (fold.IsExpanded, view.Shows("Body")));
        }, Cancellation);

    [Fact]
    public Task AnOpenFoldSitsOnAQuietFillAsync() =>
        ui.RunAsync(() =>
        {
            var fold = Fold();
            fold.IsExpanded = true;
            ViewScript.Show(fold, new object());

            Assert.Equal(Color.Parse("#09FFFFFF"), Assert.IsAssignableFrom<ISolidColorBrush>(fold.Background).Color);
        }, Cancellation);

    [Fact]
    public Task ATrailingFoldMovesItsChevronToTheEndAsync() =>
        ui.RunAsync(() =>
        {
            var fold = Fold();
            fold.Classes.Add("trailing");
            ViewScript.Show(fold, new object());
            var chevrons = fold.GetVisualDescendants().OfType<Viewbox>().ToDictionary(box => box.Name ?? string.Empty, box => box.IsEffectivelyVisible);

            Assert.Equal((false, true), (chevrons["PART_Lead"], chevrons["PART_Trail"]));
        }, Cancellation);

    [Fact]
    public Task AnAttentionFoldReadsItsFactInAmberAsync() =>
        ui.RunAsync(() =>
        {
            var fold = Fold();
            fold.Classes.Add("attention");
            ViewScript.Show(fold, new object());
            var fact = fold.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "PART_Fact");

            Assert.Equal(Color.Parse("#E5A13A"), Assert.IsAssignableFrom<ISolidColorBrush>(fact.Foreground).Color);
        }, Cancellation);

    private static Fold Fold() =>
        new()
        {
            Header = "Usage & caps",
            Fact = "USD 0.84 of USD 5",
            Content = new TextBlock { Name = "Body", Text = "Spent USD 0.84" },
        };

    private static ToggleButton Header(Fold fold) => fold.GetVisualDescendants().OfType<ToggleButton>().First();
}
