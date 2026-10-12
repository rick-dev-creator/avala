using Avala.Components.UI.Copying;
using Avala.Components.UI.Elapsed;
using Avala.Testing.UI;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Components.UI.Tests;

public sealed class CopyingScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ACopyButtonShowsItsCheckmarkOnlyOnceCopiedAsync() =>
        ui.RunAsync(() =>
        {
            var copy = new CopyButton();
            var view = ViewScript.Show(copy, new object());
            Theme.Motion.SetIsReduced(view.Window, true);
            view.Settle();
            var before = (copy.Classes.Contains(":copied"), Find(copy, "Copied").Opacity);

            copy.IsCopied = true;
            view.Settle();

            Assert.Equal((false, 0), before);
            Assert.Contains(":copied", copy.Classes);
            Assert.Equal((0, 1), (Find(copy, "Copy").Opacity, Find(copy, "Copied").Opacity));
        }, Cancellation);

    [Fact]
    public Task ACopyButtonReturnsToItsCopyIconOnceTheConfirmationHasShownAsync() =>
        ui.RunAsync(() =>
        {
            var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
            var copy = new CopyButton();
            ElapsedText.SetClock(copy, clock);
            var view = ViewScript.Show(copy, new object());
            copy.IsCopied = true;
            view.Settle();
            var confirming = copy.Classes.Contains(":copied");

            clock.Advance(CopyButton.Confirmation);
            view.Settle();

            Assert.True(confirming);
            Assert.DoesNotContain(":copied", copy.Classes);
        }, Cancellation);

    [Theory]
    [InlineData(23, "23s")]
    [InlineData(65, "1m 5s")]
    [InlineData(3720, "1h 2m")]
    public Task ElapsedTextCountsFromItsStartOnTheClockAsync(int seconds, string shown) =>
        ui.RunAsync(() =>
        {
            var elapsed = new ElapsedText { Since = DateTimeOffset.UnixEpoch };
            ElapsedText.SetClock(elapsed, new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddSeconds(seconds)));

            ViewScript.Show(elapsed, new object());

            Assert.Equal(shown, elapsed.Text);
        }, Cancellation);

    private static Control Find(Control root, string name) =>
        root.GetVisualDescendants().OfType<Control>().Single(control => control.Name == name);
}
