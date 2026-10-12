using Avala.Components.UI.Markdown;
using Avala.Testing.UI;
using Avalonia.Controls;

namespace Avala.Components.UI.Tests;

public sealed class MarkdownStylesScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task MarkdownInsideASharedScopeIsStyledByTheScopeInsteadOfItsOwnStylesAsync() =>
        ui.RunAsync(() =>
        {
            var replies = new[] { new MarkdownText { Markdown = "First reply" }, new MarkdownText { Markdown = "Second reply" } };
            var scope = new StackPanel { Children = { replies[0], replies[1] } };
            MarkdownStyles.SetAreShared(scope, true);

            var view = ViewScript.Show(scope, new object());

            Assert.Equal((1, 0, 0), (scope.Styles.Count, replies[0].Styles.Count, replies[1].Styles.Count));
            Assert.Equal([20d, 20d], Paragraphs(view).Select(paragraph => paragraph.LineHeight));
        }, Cancellation);

    [Fact]
    public Task MarkdownOutsideASharedScopeBringsItsOwnStylesAsync() =>
        ui.RunAsync(() =>
        {
            var reply = new MarkdownText { Markdown = "A reply on its own" };

            var view = ViewScript.Show(reply, new object());

            Assert.Single(reply.Styles);
            Assert.Equal(20, Assert.Single(Paragraphs(view)).LineHeight);
        }, Cancellation);

    private static IEnumerable<TextBlock> Paragraphs(ViewScript view) =>
        view.All<TextBlock>().Where(text => text.Classes.Contains("markdown-paragraph"));
}
