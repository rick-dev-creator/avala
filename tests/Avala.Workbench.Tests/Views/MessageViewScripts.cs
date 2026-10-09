using Avala.Agents.Contracts.Events;
using Avala.Components.UI.Markdown;
using Avala.Sdk;
using Avala.Testing.UI;
using Avala.Workbench.Conversation;
using Avala.Workbench.Linking;
using Avala.Workbench.Timeline;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.VisualTree;
using MarkView.Avalonia;
using MarkView.Avalonia.Rendering;

namespace Avala.Workbench.Tests.Views;

public sealed class MessageViewScripts(HeadlessUi ui)
{
    private const string Rich = """
        ## Rounding fixed

        JPY has **no minor units**, so `ToMinor` now reads the exponent:

        - JPY: 0 decimals
        - USD: 2 decimals

        ```go
        func ToMinor(amount Money) int64 {
            return amount.Units * pow10(amount.Currency.Exponent)
        }
        ```

        | Currency | Exponent |
        | --- | --- |
        | JPY | 0 |

        See [ISO 4217](https://www.iso.org/iso-4217-currency-codes.html).
        """;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AReplyIsDrawnAsMarkdownWithItsCodeInTheCodeFontAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(Message(Rich, done: true));

            Assert.Contains("Rounding fixed", Texts(view));
            Assert.Contains("JPY: 0 decimals", Texts(view));
            Assert.Single(view.All<Control>(), control => control.Classes.Contains("markdown-list"));
            Assert.Single(view.All<Control>(), control => control.Classes.Contains("markdown-table"));
            var code = Assert.Single(view.All<SelectableTextBlock>(), block => block.Classes.Contains("markdown-code"));
            Assert.StartsWith("func ToMinor(amount Money) int64 {", code.Text, StringComparison.Ordinal);
            Assert.Equal(Application.Current!.FindResource("MonoFont"), code.FontFamily);
            Assert.Single(view.All<Button>(), button => button.Classes.Contains("copy-code"));
        }, Cancellation);

    [Fact]
    public Task CopyPutsExactlyTheCodeOnTheClipboardAsync() =>
        ui.RunAsync(async () =>
        {
            var view = Screen.Show(Message(Rich, done: true));
            var copy = Assert.Single(view.All<Button>(), button => button.Classes.Contains("copy-code"));

            view.Click(copy);
            var copied = await view.Window.Clipboard!.TryGetTextAsync();

            Assert.Equal("func ToMinor(amount Money) int64 {\n    return amount.Units * pow10(amount.Currency.Exponent)\n}", copied);
            Assert.Equal("Copied", copy.Content);
        }, Cancellation);

    [Fact]
    public Task WhileStreamingOnlyTheLastBlockIsDrawnAgainAsync() =>
        ui.RunAsync(() =>
        {
            var message = Message("## Rounding fixed\n\nJPY has no minor", done: false);
            var view = Screen.Show(message);
            var settled = Viewer(view, "Settled");
            var drawn = settled.Content;

            message.Update(new MessageEntry("m", "## Rounding fixed\n\nJPY has no minor units, so", Option<ItemOutcome>.None));
            view.Settle();
            var kept = ReferenceEquals(drawn, settled.Content);
            message.Update(new MessageEntry("m", "## Rounding fixed\n\nJPY has no minor units, so\n\n```go\nfunc ToMinor(", Option<ItemOutcome>.None));
            view.Settle();
            var streamingCode = view.All<SelectableTextBlock>().Any(block => block.Classes.Contains("markdown-code") && block.Text == "func ToMinor(");

            message.Update(new MessageEntry("m", "## Rounding fixed\n\nJPY has no minor units, so\n\n```go\nfunc ToMinor()\n```\n", ItemOutcome.Succeeded));
            view.Settle();

            Assert.True(kept);
            Assert.True(streamingCode);
            Assert.False(Viewer(view, "Tail").IsVisible);
            Assert.Equal("## Rounding fixed\n\nJPY has no minor units, so\n\n```go\nfunc ToMinor()\n```\n", settled.Markdown);
        }, Cancellation);

    [Fact]
    public Task ACaretSitsUnderTheLiveBlockUntilTheReplyEndsAndHoldsStillWithReducedMotionAsync() =>
        ui.RunAsync(() =>
        {
            var message = Message("JPY has no minor", done: false);
            var view = Screen.Show(message);
            var caret = view.Find("Caret");
            var streaming = (view.Shows("Caret"), caret.Classes.Contains("caret"), caret.Bounds.Top >= Viewer(view, "Tail").Bounds.Bottom);
            Components.UI.Theme.Motion.SetIsReduced(view.Window, true);
            view.Settle();
            var still = caret.Opacity;

            message.Update(new MessageEntry("m", "JPY has no minor units.", ItemOutcome.Succeeded));
            view.Settle();

            Assert.Equal((true, true, true), streaming);
            Assert.Equal(1, still);
            Assert.False(view.Shows("Caret"));
        }, Cancellation);

    [Fact]
    public Task AWebLinkOpensThroughTheLinkOpenerAsync() =>
        ui.RunAsync(async () =>
        {
            var opener = new FakeLinks();
            var view = Screen.Show(Message(Rich, done: true, opener));

            Click(view, "https://www.iso.org/iso-4217-currency-codes.html");
            await OpenedAsync(view);

            Assert.Equal([new Uri("https://www.iso.org/iso-4217-currency-codes.html")], opener.Opened);
            Assert.False(view.Shows("LinkNotice"));
        }, Cancellation);

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("docs/money.md")]
    public Task AnyOtherLinkOpensNothingAndSaysWhyAsync(string link) =>
        ui.RunAsync(async () =>
        {
            var opener = new FakeLinks();
            var view = Screen.Show(Message($"See [the notes]({link}).", done: true, opener));

            Click(view, link);
            await OpenedAsync(view);

            Assert.Empty(opener.Opened);
            Assert.True(view.Shows("LinkNotice"));
            Assert.Equal($"Avala opens only web links, so {link} was not opened.", view.TextOf("LinkNotice"));
        }, Cancellation);

    [Fact]
    public Task ARemoteImageIsNeverFetchedAndShowsItsAlternativeTextAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(Message("The chart: ![rounding chart](https://example.com/chart.png)", done: true));

            Assert.Empty(view.All<Image>());
            Assert.Contains(Texts(view), text => text.Contains("[rounding chart]", StringComparison.Ordinal));
        }, Cancellation);

    private static MessageViewModel Message(string text, bool done, FakeLinks? opener = null) =>
        new(new MessageEntry("m", text, done ? ItemOutcome.Succeeded : Option<ItemOutcome>.None), new Links(opener ?? new FakeLinks()));

    private static MarkdownViewer Viewer(ViewScript view, string name) =>
        view.Window.GetVisualDescendants().OfType<MarkdownViewer>().Single(viewer => viewer.Name == name);

    private static void Click(ViewScript view, string link) =>
        Viewer(view, "Settled").RaiseEvent(new LinkClickedEventArgs(link) { RoutedEvent = MarkdownViewer.LinkClickedEvent });

    private static async Task OpenedAsync(ViewScript view)
    {
        var message = (MessageViewModel)view.Find<MarkdownText>("Text").DataContext!;
        await (message.OpenLinkCommand.ExecutionTask ?? Task.CompletedTask);
        view.Settle();
    }

    private static IReadOnlyList<string> Texts(ViewScript view) =>
        [.. view.All<TextBlock>().Select(text => text.Inlines is { Count: > 0 } inlines ? inlines.Text ?? string.Empty : text.Text ?? string.Empty).Where(text => text.Length > 0)];
}
