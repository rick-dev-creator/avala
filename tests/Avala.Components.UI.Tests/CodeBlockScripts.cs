using Avala.Components.UI.Markdown;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Avala.Components.UI.Tests;

public sealed class CodeBlockScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ACodeBlockShowsItsCodeAtOnceAndColorsItInTheThemesSyntaxColorsOnceTokenizedAsync() =>
        ui.RunAsync(async () =>
        {
            var code = Code(nameof(ACodeBlockShowsItsCodeAtOnceAndColorsItInTheThemesSyntaxColorsOnceTokenizedAsync), 40);
            var window = new Window { Width = 640, Height = 480, RequestedThemeVariant = ThemeVariant.Light, Content = new MarkdownText { Markdown = Fenced(code) } };
            window.Show();
            var block = Assert.Single(window.GetVisualDescendants().OfType<CodeText>());
            var plain = (block.Text, block.IsPainted, block.Bounds.Height);

            await CodeBlocks.PaintedAsync(block);
            var view = ViewScript.Present(window, new object());

            Assert.Equal((code, false), (plain.Text, plain.IsPainted));
            Assert.Equal((code, plain.Height), (block.Text, block.Bounds.Height));
            Assert.Equal("keyword", block.RoleAt(code.IndexOf("var", StringComparison.Ordinal)));
            Assert.Equal(Application.Current!.FindResource(ThemeVariant.Light, "SyntaxKeywordBrush"), CodeBlocks.ForegroundOf(block, "var"));

            view.Window.RequestedThemeVariant = ThemeVariant.Dark;
            view.Settle();

            Assert.Equal(Application.Current!.FindResource(ThemeVariant.Dark, "SyntaxKeywordBrush"), CodeBlocks.ForegroundOf(block, "var"));
        }, Cancellation);

    [Fact]
    public Task ACodeBlockSeenBeforeIsColoredAsSoonAsItIsShownAsync() =>
        ui.RunAsync(async () =>
        {
            var code = Code(nameof(ACodeBlockSeenBeforeIsColoredAsSoonAsItIsShownAsync), 20);
            await CodeBlocks.PaintedAsync(Block(Show(code)));

            var again = Block(Show(code));

            Assert.True(again.IsPainted);
        }, Cancellation);

    [Fact]
    public Task ACodeBlockLongerThanTheHighlightingLimitStaysPlainAndSaysSoAsync() =>
        ui.RunAsync(() =>
        {
            var code = Code(nameof(ACodeBlockLongerThanTheHighlightingLimitStaysPlainAndSaysSoAsync), CodeColors.HighlightedLines + 1);

            var view = Show(code);

            Assert.Equal((code, false), (Block(view).Text, Block(view).IsPainted));
            Assert.Contains(CopyableCode.LongBlockHint, view.VisibleTexts);
        }, Cancellation);

    public static string Code(string name, int lines) =>
        string.Join('\n', Enumerable.Range(0, lines).Select(line => line % 2 == 0 ? $"// {name} {line}" : $"var total{line} = \"sum\" + {line};"));

    public static ViewScript Show(string code) => ViewScript.Show(new MarkdownText { Markdown = Fenced(code) }, new object());

    public static CodeText Block(ViewScript view) => Assert.Single(view.All<CodeText>());

    private static string Fenced(string code) => $"```csharp\n{code}\n```\n";
}
