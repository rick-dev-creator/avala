using Avala.Testing.UI;
using Avala.Workbench.NewJob;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;

namespace Avala.Workbench.Tests.Views;

public sealed class ConnectionOptionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AConnectionShowsItsNameWithItsReadingInAmberNearItsLimitAndNoReadingWhenItHasNoneAsync() =>
        ui.RunAsync(() =>
        {
            var near = Screen.Show(new DesignConnectionOptionViewModel("claude", "99% of the 7-day window used", true));
            var auto = Screen.Show(new DesignConnectionOptionViewModel(NewJobPhrases.Auto, string.Empty, false));

            Assert.Equal(("claude", "99% of the 7-day window used", true), (near.TextOf("Connection"), near.TextOf("Reading"), near.HasClass("Reading", "attention")));
            Assert.Equal((NewJobPhrases.Auto, false), (auto.TextOf("Connection"), auto.Shows("Reading")));
        }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task AHighlightedOptionKeepsItsNameAndReadingLegibleOnTheHighlightAsync(string name) =>
        ui.RunAsync(() =>
        {
            var variant = name == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            var selected = Item("selected", "no usage reported yet", nearLimit: false);
            selected.IsSelected = true;
            var hovered = Item("hovered", "31% of the 5-hour window used", nearLimit: false);
            ((IPseudoClasses)hovered.Classes).Set(":pointerover", true);
            var near = Item("near", "88% of the 5-hour window used", nearLimit: true);
            near.IsSelected = true;
            ((IPseudoClasses)near.Classes).Set(":pointerover", true);
            var view = Screen.Show(new StackPanel { Children = { Item("plain", "no usage reported yet", nearLimit: false), selected, hovered, near } }, variant, 400, 300);

            var texts = Legibility.Texts(view.Window, Legibility.Resource("SurfaceWindowBrush", variant));

            Assert.Equal(8, texts.Count);
            Assert.All(texts, text => Assert.True(text.Ratio >= (Names.Contains(text.Text) ? LegibleName : LegibleCaption), $"\"{text.Text}\" is {text.Foreground} on {text.Background}: {text.Ratio:F2}:1"));
        }, TestContext.Current.CancellationToken);

    private const double LegibleName = 4.5;

    private const double LegibleCaption = 3;

    private static readonly HashSet<string> Names = ["plain", "selected", "hovered", "near"];

    private static ComboBoxItem Item(string name, string reading, bool nearLimit) =>
        new() { Content = new DesignConnectionOptionViewModel(name, reading, nearLimit) };
}
