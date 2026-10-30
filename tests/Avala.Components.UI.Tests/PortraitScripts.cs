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
            Assert.True(view.Shows("Silhouette"));
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
    public Task EachMoodHasItsOwnFaceAsync() =>
        ui.RunAsync(() =>
        {
            var portrait = new Portrait();
            var view = ViewScript.Show(portrait, new object());
            var faces = Enum.GetValues<Mood>().Select(mood =>
            {
                portrait.Mood = mood;
                view.Settle();
                return (view.Find<Avalonia.Controls.Shapes.Path>("Eyes").Data!.Bounds, view.Find<Avalonia.Controls.Shapes.Path>("Mouth").Data!.Bounds);
            }).ToList();

            Assert.Equal(faces.Count, faces.Distinct().Count());
        }, Cancellation);

    [Fact]
    public Task AStatusConvertsToTheMoodItShowsAsync() =>
        ui.RunAsync(() =>
            Assert.Equal(Mood.Asking, MoodOf.Status.Convert(StatusKind.NeedsYou, typeof(Mood), null, System.Globalization.CultureInfo.InvariantCulture)), Cancellation);
}
