using Avala.Components.Status;
using Avala.Components.UI.States;
using Avala.Components.UI.Theme;
using Avala.Testing.UI;
using Avalonia.Controls;
using Avalonia.Media;

namespace Avala.Components.UI.Tests;

public sealed class PortraitScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task APortraitShowsOnlyItsMoodAsync() =>
        ui.RunAsync(() =>
        {
            var portrait = new Portrait { Mood = Mood.Worried };
            var view = ViewScript.Show(portrait, new object());

            Assert.Equal(
                (false, false, true, false),
                (portrait.Classes.Contains(":focused"), portrait.Classes.Contains(":asking"), portrait.Classes.Contains(":worried"), portrait.Classes.Contains(":pleased")));
            Assert.True(view.Shows("Face"));
        }, Cancellation);

    [Fact]
    public Task APortraitChangesItsFrameWithItsMoodAsync() =>
        ui.RunAsync(() =>
        {
            var portrait = new Portrait { Mood = Mood.Focused };
            var view = ViewScript.Show(portrait, new object());
            var frame = view.Find<Border>("Frame");
            var focused = ((ISolidColorBrush)frame.BorderBrush!).Color;

            portrait.Mood = Mood.Asking;
            view.Settle();

            Assert.NotEqual(focused, ((ISolidColorBrush)frame.BorderBrush).Color);
            Assert.Equal((false, true), (portrait.Classes.Contains(":focused"), portrait.Classes.Contains(":asking")));
        }, Cancellation);

    [Fact]
    public Task APortraitBreathesAsync() =>
        ui.RunAsync(() =>
        {
            var view = ViewScript.Show(new Portrait { Mood = Mood.Focused }, new object());

            Assert.Equal(MotionLoop.Breathe, view.LoopOf("Figure"));
            Assert.True(MotionCatalog.Loops.ContainsKey(MotionLoop.Breathe));
        }, Cancellation);

    [Fact]
    public Task EachMoodShowsItsOwnPictureAsync() =>
        ui.RunAsync(() =>
        {
            var portrait = new Portrait();
            var view = ViewScript.Show(portrait, new object());
            var shown = Enum.GetValues<Mood>().Select(mood =>
            {
                portrait.Mood = mood;
                view.Settle();
                return (Mood: mood, Shown: view.Find<Image>("Face").Source, Picture: portrait.Picture);
            }).ToList();

            Assert.All(shown, face => Assert.Same(face.Picture, face.Shown));
            Assert.Equal(shown.Count, shown.Select(face => face.Shown).Distinct().Count());
            Assert.All(Enum.GetValues<Mood>(), mood => Assert.True(Avalonia.Platform.AssetLoader.Exists(Portrait.PictureOf(mood)), $"{mood} has no picture"));
            Assert.Equal(["asking.png", "focused.png", "pleased.png", "worried.png"], Enum.GetValues<Mood>().Select(mood => Portrait.PictureOf(mood).Segments[^1]).Order());
        }, Cancellation);

    [Fact]
    public Task AStatusConvertsToTheMoodItShowsAsync() =>
        ui.RunAsync(() =>
            Assert.Equal(Mood.Asking, MoodOf.Status.Convert(StatusKind.NeedsYou, typeof(Mood), null, System.Globalization.CultureInfo.InvariantCulture)), Cancellation);
}
