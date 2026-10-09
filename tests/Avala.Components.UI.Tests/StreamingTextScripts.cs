using System.Globalization;
using Avala.Components.UI.States;
using Avala.Components.UI.Streaming;
using Avala.Testing.UI;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Media;

namespace Avala.Components.UI.Tests;

public sealed class StreamingTextScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TextThatArrivesWhileLiveFadesInAfterWhatIsAlreadyShownAsync() =>
        ui.RunAsync(() =>
        {
            var text = new StreamingText { IsLive = true };
            var view = ViewScript.Show(text, new object());

            text.Stream = "Totals now";
            text.Stream = "Totals now round";
            view.Settle();

            var runs = text.Inlines!.OfType<Run>().ToList();
            Assert.Equal(["", "Totals now", " round"], runs.Select(run => run.Text));
            Assert.All(runs.Skip(1), run => Assert.NotEmpty(Assert.IsType<SolidColorBrush>(run.Foreground).Transitions!));
            Assert.True(text.ShowsCaret);
        }, Cancellation);

    [Fact]
    public Task OnlyTheLatestChunksStaySeparateSoLongMessagesStayLightAsync() =>
        ui.RunAsync(() =>
        {
            var text = new StreamingText { IsLive = true };
            ViewScript.Show(text, new object());

            foreach (var count in Enumerable.Range(1, 40))
            {
                text.Stream = string.Concat(Enumerable.Repeat("word ", count));
            }

            Assert.Equal(8, text.Arriving);
            Assert.Equal(string.Concat(Enumerable.Repeat("word ", 40)), string.Concat(text.Inlines!.OfType<Run>().Select(run => run.Text)));
        }, Cancellation);

    [Fact]
    public Task WhenTheStreamEndsTheTextSettlesWithoutACaretAsync() =>
        ui.RunAsync(() =>
        {
            var text = new StreamingText { IsLive = true, Stream = "Totals" };
            ViewScript.Show(text, new object());

            text.Stream = "Totals round to whole yen.";
            text.IsLive = false;

            Assert.Equal((0, false), (text.Arriving, text.ShowsCaret));
            Assert.Equal("Totals round to whole yen.", Assert.Single(text.Inlines!.OfType<Run>()).Text);
        }, Cancellation);

    [Fact]
    public Task TextThatIsNotLiveOrIsRewrittenShowsWholeAtOnceAsync() =>
        ui.RunAsync(() =>
        {
            var text = new StreamingText { Stream = "Done already." };
            ViewScript.Show(text, new object());
            var still = text.Arriving;

            text.IsLive = true;
            text.Stream = "Something else";

            Assert.Equal((0, 0), (still, text.Arriving));
            Assert.Equal("Something else", text.Inlines!.OfType<Run>().First().Text);
        }, Cancellation);
}

public sealed class StateConverterScripts
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(TextWrapping.Wrap, "Wrap", true)]
    [InlineData(TextWrapping.Wrap, "NoWrap, WrapWithOverflow", false)]
    [InlineData(TextWrapping.NoWrap, "Wrap,NoWrap", true)]
    public void AnEnumMatchesAnyOfTheNamesItIsGiven(TextWrapping value, string names, bool matches) =>
        Assert.Equal(matches, EnumIs.Any.Convert(value, typeof(bool), names, Culture));

    [Fact]
    public void ANullSelectionIsNeverWrittenBackSoAHiddenPageStaysSelected()
    {
        var page = new object();

        Assert.Equal((page, page, BindingOperations.DoNothing), (Kept.Selection.Convert(page, typeof(object), null, Culture), Kept.Selection.ConvertBack(page, typeof(object), null, Culture), Kept.Selection.ConvertBack(null, typeof(object), null, Culture)));
    }

    [Fact]
    public void TwoValuesAreTheSameOnlyWhenTheyAreOneObject()
    {
        var page = new object();

        Assert.Equal((true, false, false), (Same.Item.Convert([page, page], typeof(bool), null, Culture), Same.Item.Convert([page, new object()], typeof(bool), null, Culture), Same.Item.Convert([null, null], typeof(bool), null, Culture)));
    }
}

public sealed class ThemeIconScripts(HeadlessUi ui)
{
    [Fact]
    public Task AKindNamesItsIconFromTheThemeAndAnUnknownKindHasNoneAsync() =>
        ui.RunAsync(() =>
        {
            var view = ViewScript.Show(new Border(), new object());

            Assert.Same(view.Window.FindResource("IconCommand"), ThemeIcon.Named.Convert("Command", typeof(Geometry), "Icon", CultureInfo.InvariantCulture));
            Assert.Null(ThemeIcon.Named.Convert("Nothing", typeof(Geometry), "Icon", CultureInfo.InvariantCulture));
        }, TestContext.Current.CancellationToken);
}
