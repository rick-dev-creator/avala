using Avala.Components.UI.Theme;
using Avala.Components.UI.Voice;
using Avala.Testing.UI;
using Avalonia.Controls;

namespace Avala.Components.UI.Tests;

public sealed class VoiceTextScripts(HeadlessUi ui)
{
    private const string Line = "An hour? Smells like a token nobody refreshes.";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.25, 9)]
    [InlineData(0.5, 21)]
    [InlineData(1, 46)]
    public void ALineIsCutAtTheStartOfTheFirstWordNotYetSaid(double revealed, int cut) =>
        Assert.Equal(cut, VoiceText.Cut(Line, revealed));

    [Fact]
    public void ALineTakesFortyFiveMillisecondsAWordAndNeverMoreThanTwoAndAHalfSeconds() =>
        Assert.Equal((TimeSpan.FromMilliseconds(360), VoiceText.Longest), (VoiceText.Duration(Line), VoiceText.Duration(string.Join(' ', Enumerable.Repeat("word", 100)))));

    [Fact]
    public Task ALineArrivesWordByWordInTheSpaceItWillTakeAsync() =>
        ui.RunAsync(() =>
        {
            var (view, voice) = Spoken(reduced: false);
            var start = (voice.Revealed, voice.Bounds.Size);

            view.Elapse(TimeSpan.FromMilliseconds(180));
            var halfway = voice.Revealed;
            view.Elapse(TimeSpan.FromMilliseconds(400));

            Assert.Equal(0, start.Revealed);
            Assert.InRange(halfway, 0.3, 0.7);
            Assert.Equal((1d, start.Size), (voice.Revealed, voice.Bounds.Size));
        }, Cancellation);

    [Fact]
    public Task WithReducedMotionTheWholeLineShowsAtOnceAsync() =>
        ui.RunAsync(() =>
        {
            var (_, voice) = Spoken(reduced: true);

            Assert.Equal(1, voice.Revealed);
        }, Cancellation);

    [Fact]
    public Task ANewLineIsSaidFromItsFirstWordAsync() =>
        ui.RunAsync(() =>
        {
            var (view, voice) = Spoken(reduced: false);
            view.Elapse(TimeSpan.FromMilliseconds(600));

            voice.Text = "Two tests went red.";
            view.Settle();

            Assert.Equal(0, voice.Revealed);
        }, Cancellation);

    private static (ViewScript View, VoiceText Voice) Spoken(bool reduced)
    {
        var view = ViewScript.Show(new Border(), new object());
        Motion.SetIsReduced(view.Window, reduced);
        var voice = new VoiceText { Text = Line, Width = 300, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        view.Window.Content = voice;

        return (view.Settle(), voice);
    }
}
