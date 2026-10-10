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

    [Fact]
    public Task WhenHeaderAndFactDoNotFitTheShorterStaysWholeAndTheLongerGivesWayAsync() =>
        ui.RunAsync(() =>
        {
            var fold = new Fold
            {
                Header = new TextBlock { Text = "You denied: run set -o pipefail; node --test --test-reporter=tap | grep -E '^# (pass|fail)'", TextTrimming = TextTrimming.CharacterEllipsis },
                Fact = "by you",
                Width = 300,
            };
            ViewScript.Show(fold, new object());
            var header = Assert.IsType<TextBlock>(fold.Header);
            var fact = fold.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "PART_Fact");

            Assert.True(fact.Bounds.Width >= Natural(fact) - 0.5, $"the fact is {fact.Bounds.Width} wide of {Natural(fact)}");
            Assert.True(header.Bounds.Width < Natural(header) && header.Bounds.Right <= fact.Bounds.Left + 0.5, $"the header is {header.Bounds.Width} wide of {Natural(header)}");
        }, Cancellation);

    private static double Natural(TextBlock text)
    {
        var probe = new TextBlock { Text = text.Text, FontSize = text.FontSize, FontFamily = text.FontFamily, FontWeight = text.FontWeight };
        probe.Measure(Avalonia.Size.Infinity);

        return probe.DesiredSize.Width;
    }

    private static Fold Fold() =>
        new()
        {
            Header = "Usage & caps",
            Fact = "USD 0.84 of USD 5",
            Content = new TextBlock { Name = "Body", Text = "Spent USD 0.84" },
        };

    private static ToggleButton Header(Fold fold) => fold.GetVisualDescendants().OfType<ToggleButton>().First();
}
