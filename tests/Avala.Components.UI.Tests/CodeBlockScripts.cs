using Avala.Components.UI.Markdown;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Avala.Components.UI.Tests;

public sealed class CodeBlockScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ACodeBlockShowsItsCodeAtOnceAndColorsItInVisualStudioCodesColorsOnceTokenizedAsync() =>
        ui.RunAsync(async () =>
        {
            var code = Code(nameof(ACodeBlockShowsItsCodeAtOnceAndColorsItInVisualStudioCodesColorsOnceTokenizedAsync), 40);
            var window = new Window { Width = 640, Height = 480, RequestedThemeVariant = ThemeVariant.Light, Content = new MarkdownText { Markdown = Fenced(code) } };
            window.Show();
            var block = Assert.Single(window.GetVisualDescendants().OfType<CodeText>());
            var plain = (block.Text, block.IsPainted, block.Bounds.Height);

            await CodeBlocks.PaintedAsync(block);
            var view = ViewScript.Present(window, new object());

            Assert.Equal((code, false), (plain.Text, plain.IsPainted));
            Assert.Equal((code, plain.Height), (block.Text, block.Bounds.Height));
            Assert.Equal((Colors.Blue, Color.Parse("#A31515")), (CodeBlocks.ColorOf(block, "var"), CodeBlocks.ColorOf(block, "\"sum\"")));

            view.Window.RequestedThemeVariant = ThemeVariant.Dark;
            view.Settle();

            Assert.Equal((Color.Parse("#569CD6"), Color.Parse("#CE9178")), (CodeBlocks.ColorOf(block, "var"), CodeBlocks.ColorOf(block, "\"sum\"")));
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
    public Task ALongCodeBlockIsColoredToEveryLineAsync() =>
        ui.RunAsync(async () =>
        {
            var code = Code(nameof(ALongCodeBlockIsColoredToEveryLineAsync), 2_000);
            var view = Show(code);

            await CodeBlocks.PaintedAsync(Block(view));

            Assert.Equal(Color.Parse("#569CD6"), CodeBlocks.ColorOf(Block(view), "var", last: true));
        }, Cancellation);

    [Fact]
    public Task ACodeBlockOfThirtyLinesOrMoreNumbersEachOfItsLinesEvenWhenOneWrapsAsync() =>
        ui.RunAsync(() =>
        {
            var code = string.Join('\n', Enumerable.Range(1, CodeText.NumberedLines).Select(line => line == 2 ? new string('x', 400) : $"line {line}"));
            var view = Show(code);

            var numbers = Block(view).Numbers();

            Assert.Equal(["1", "2", string.Empty], numbers.Take(3));
            Assert.Equal(Enumerable.Range(1, CodeText.NumberedLines).Select(line => line.ToString(System.Globalization.CultureInfo.InvariantCulture)), numbers.Where(number => number.Length > 0));
        }, Cancellation);

    [Fact]
    public Task AShortCodeBlockHasNoLineNumbersAsync() =>
        ui.RunAsync(() => Assert.Empty(Block(Show(Code(nameof(AShortCodeBlockHasNoLineNumbersAsync), CodeText.NumberedLines - 1))).Numbers()), Cancellation);

    public static string Code(string name, int lines) =>
        string.Join('\n', Enumerable.Range(0, lines).Select(line => line % 2 == 0 ? $"// {name} {line}" : $"var total{line} = \"sum\" + {line};"));

    public static ViewScript Show(string code) => ViewScript.Show(new MarkdownText { Markdown = Fenced(code) }, new object());

    public static CodeText Block(ViewScript view) => Assert.Single(view.All<CodeText>());

    private static string Fenced(string code) => $"```csharp\n{code}\n```\n";
}
