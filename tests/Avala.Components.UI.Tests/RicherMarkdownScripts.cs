using Avala.Components.UI.Markdown;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;

namespace Avala.Components.UI.Tests;

public sealed class RicherMarkdownScripts(HeadlessUi ui)
{
    private const string WideTable = "| Currency | Exponent | Before | After | Rounded once | Notes on the change |\n|---|---|---|---|---|---|\n| JPY | 0 | ¥1,100 | ¥1,080 | yes | the tax used to be rounded in sen and then again in yen |\n";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("NOTE", "markdown-alert-note")]
    [InlineData("TIP", "markdown-alert-tip")]
    [InlineData("IMPORTANT", "markdown-alert-important")]
    [InlineData("WARNING", "markdown-alert-warning")]
    [InlineData("CAUTION", "markdown-alert-caution")]
    public Task AGitHubCalloutIsDrawnAsAnAlertOfItsKindAsync(string kind, string style) =>
        ui.RunAsync(() =>
        {
            var view = ViewScript.Show(new MarkdownText { Markdown = $"> [!{kind}]\n> Totals are rounded once.\n" }, new object());

            Assert.Contains(view.All<Border>(), border => border.Classes.Contains(style));
            Assert.DoesNotContain($"[!{kind}]", view.VisibleTexts);
        }, Cancellation);

    [Fact]
    public Task AFootnoteIsListedUnderTheReplyAsync() =>
        ui.RunAsync(() =>
        {
            var view = ViewScript.Show(new MarkdownText { Markdown = "See the spec[^1].\n\n[^1]: ISO 4217 lists each exponent.\n" }, new object());

            Assert.Contains(view.All<StackPanel>(), panel => panel.Classes.Contains("markdown-footnote-group"));
            Assert.DoesNotContain(view.VisibleTexts, text => text.Contains("[^1]", StringComparison.Ordinal));
        }, Cancellation);

    [Fact]
    public Task InlineAndBlockMathAreTypesetAsync() =>
        ui.RunAsync(() =>
        {
            var view = ViewScript.Show(new MarkdownText { Markdown = "The rate is $r = 0.08$ and the total is\n\n$$\nt = \\sum_i p_i (1 + r)\n$$\n" }, new object());

            var formulas = view.All<MathFormula>();

            Assert.Equal(["r = 0.08", "t = \\sum_i p_i (1 + r)"], formulas.Select(formula => formula.LaTeX));
            Assert.All(formulas, formula => Assert.True(formula.IsTypeset));
        }, Cancellation);

    [Fact]
    public Task MathThatDoesNotParseIsShownAsItsSourceAsync() =>
        ui.RunAsync(() =>
        {
            var view = ViewScript.Show(new MarkdownText { Markdown = "$$\n\\frac{1}{\n$$\n" }, new object());

            Assert.Empty(view.All<MathFormula>());
            Assert.Contains("\\frac{1}{", view.VisibleTexts);
        }, Cancellation);

    [Fact]
    public Task AWideTableKeepsItsColumnsAndScrollsSidewaysAsync() =>
        ui.RunAsync(() =>
        {
            var view = ViewScript.Show(new MarkdownText { Markdown = WideTable }, new object());
            view.Window.Width = 420;
            view.Settle();

            var scroller = Assert.Single(view.All<ScrollViewer>(), found => found.Classes.Contains("markdown-table-scroller"));

            Assert.True(scroller.Extent.Width > scroller.Viewport.Width);
        }, Cancellation);

    [Fact]
    public Task TheWheelOverAWideTableStillScrollsTheConversationAsync() =>
        ui.RunAsync(() =>
        {
            var reply = new MarkdownText { Markdown = WideTable + "\n" + string.Concat(Enumerable.Repeat("More text below the table.\n\n", 40)) };
            var conversation = new ScrollViewer { Content = reply };
            var view = ViewScript.Show(conversation, new object());
            view.Window.Width = 420;
            view.Window.Height = 400;
            view.Settle();
            var table = Assert.Single(view.All<ScrollViewer>(), found => found.Classes.Contains("markdown-table-scroller"));

            view.Window.MouseWheel(table.TranslatePoint(new Point(20, 20), view.Window)!.Value, new Vector(0, -3));
            view.Settle();

            Assert.True(conversation.Offset.Y > 0);
        }, Cancellation);
}
