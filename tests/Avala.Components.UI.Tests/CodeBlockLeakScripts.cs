using Avala.Components.UI.Markdown;
using Avala.Testing.UI;
using Avalonia.Controls;

namespace Avala.Components.UI.Tests;

[Collection(Leaks.Collection)]
public sealed class CodeBlockLeakScripts(HeadlessUi ui)
{
    [Fact]
    public Task ShowingAndClosingACodeBlockTwentyTimesReturnsMemoryToItsBaselineAsync() =>
        ui.RunAsync(async () =>
        {
            var code = CodeBlockScripts.Code(nameof(ShowingAndClosingACodeBlockTwentyTimesReturnsMemoryToItsBaselineAsync), 120);
            var view = ViewScript.Show(new TextBlock(), new object());
            var cached = 0L;

            var (baseline, after) = await Leaks.ManagedAcrossAsync(async () =>
            {
                view.Window.Content = new MarkdownText { Markdown = $"```csharp\n{code}\n```\n" };
                view.Settle();
                await CodeBlocks.PaintedAsync(Assert.Single(view.All<CodeText>()));
                view.Window.Content = null;
                view.Settle();
                cached = cached == 0 ? CodeColors.CachedBytes : cached;
            });

            Assert.InRange(CodeColors.CachedBytes, 1, Math.Min(cached, CodeColors.CacheBudget));
            Assert.InRange(after - baseline, long.MinValue, Leaks.Tolerance);
        }, TestContext.Current.CancellationToken);
}
