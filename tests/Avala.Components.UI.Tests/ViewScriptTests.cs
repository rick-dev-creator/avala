using Avala.Testing.UI;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Components.UI.Tests;

public sealed class ViewScriptTests(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ClickingAControlCoveredByAnotherFailsAndNamesWhatIsUnderThePointerAsync() =>
        ui.RunAsync(() =>
        {
            var clicks = 0;
            var view = ViewScript.Show(
                new Grid
                {
                    Children =
                    {
                        new Button { Name = "Covered", Content = "Save", Command = new RelayCommand(() => clicks++) },
                        new Border { Name = "Cover", Background = Brushes.Black },
                    },
                },
                new object());

            var refused = Assert.Throws<InvalidOperationException>(() => view.Click("Covered"));

            Assert.Contains("Border Cover", refused.Message, StringComparison.Ordinal);
            Assert.Equal(0, clicks);
        }, Cancellation);

    [Fact]
    public Task TypingIntoAControlThatCannotTakeTheFocusFailsAsync() =>
        ui.RunAsync(() =>
        {
            var view = ViewScript.Show(new TextBox { Name = "Note", IsEnabled = false }, new object());

            Assert.Throws<InvalidOperationException>(() => view.Type("Note", "lost"));
        }, Cancellation);

    [Fact]
    public Task MotionHoldsItsFirstFrameSoAClickLandsWhereTheScriptReadTheTargetAsync() =>
        ui.RunAsync(() =>
        {
            var panel = new Border { Name = "Panel", Classes = { "fold-in" }, Height = 40, Background = Brushes.Black };
            var view = ViewScript.Show(panel, new object());

            view.Click(panel);

            Assert.Equal((0d, 1.4), (panel.Opacity, panel.RenderTransform!.Value.M22));
        }, Cancellation);
}
