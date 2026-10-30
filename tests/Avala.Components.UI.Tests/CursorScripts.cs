using Avala.Components.UI.Theme;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Avala.Components.UI.Tests;

public sealed class CursorScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheCursorStartsOnTheChosenItemThenGlidesToTheNextChoiceAsync() =>
        ui.RunAsync(() =>
        {
            var (view, list) = Listed(reduced: false);
            var first = Y(list);

            list.SelectedIndex = 2;
            view.Settle();
            var gliding = Y(list);
            view.Elapse(Cursor.Glide + TimeSpan.FromMilliseconds(50));

            Assert.Equal(Top(list, 0), first);
            Assert.InRange(gliding, Top(list, 0), Top(list, 2) - 1);
            Assert.Equal(Top(list, 2), Y(list), 1);
        }, Cancellation);

    [Fact]
    public Task WithReducedMotionTheCursorJumpsToTheChoiceAsync() =>
        ui.RunAsync(() =>
        {
            var (view, list) = Listed(reduced: true);

            list.SelectedIndex = 2;
            view.Settle();

            Assert.Equal(Top(list, 2), Y(list), 1);
        }, Cancellation);

    [Fact]
    public Task WithoutAChoiceTheCursorIsHiddenAsync() =>
        ui.RunAsync(() =>
        {
            var (view, list) = Listed(reduced: false);

            list.SelectedIndex = -1;
            view.Settle();

            Assert.False(Mark(list).IsVisible);
        }, Cancellation);

    [Fact]
    public Task WhenTheChosenItemMovesWithoutANewChoiceTheCursorFollowsAtOnceAsync() =>
        ui.RunAsync(() =>
        {
            var pages = new System.Collections.ObjectModel.ObservableCollection<string>(["Jobs", "Overview", "Usage"]);
            var (view, list) = Listed(reduced: false, pages);
            list.SelectedIndex = 1;
            view.Elapse(Cursor.Glide + TimeSpan.FromMilliseconds(50));

            pages.Insert(0, "Inbox");
            view.Settle();

            Assert.Equal(Top(list, 2), Y(list), 1);
        }, Cancellation);

    [Fact]
    public Task ALayoutElsewhereInTheWindowDoesNotCutTheGlideShortAsync() =>
        ui.RunAsync(() =>
        {
            var (view, list) = Listed(reduced: false);
            var elsewhere = new TextBlock { Text = "before" };
            view.Window.Content = new StackPanel { Children = { elsewhere, Detached(list) } };
            view.Settle();

            list.SelectedIndex = 3;
            view.Settle();
            view.Elapse(TimeSpan.FromMilliseconds(60));
            elsewhere.Text = "a longer line that lays the window out again";
            view.Settle();

            Assert.InRange(Y(list), Top(list, 0), Top(list, 3) - 1);
        }, Cancellation);

    [Fact]
    public Task AHiddenListHidesItsCursorAsync() =>
        ui.RunAsync(() =>
        {
            var (view, list) = Listed(reduced: false);

            list.IsVisible = false;
            view.Settle();

            Assert.False(Mark(list).IsVisible);
        }, Cancellation);

    private static ListBox Detached(ListBox list)
    {
        ((ContentControl)list.Parent!).Content = null;

        return list;
    }

    private static double Y(ListBox list) => ((TranslateTransform)Mark(list).RenderTransform!).Y;

    private static Border Mark(ListBox list) => Cursor.MarkOf(list).Match(mark => mark, () => throw new InvalidOperationException("The list has no cursor."));

    private static double Top(ListBox list, int index) => list.ContainerFromIndex(index)!.TranslatePoint(default, list)!.Value.Y;

    private static (ViewScript View, ListBox List) Listed(bool reduced, IEnumerable<string>? pages = null)
    {
        var view = ViewScript.Show(new Border(), new object());
        Motion.SetIsReduced(view.Window, reduced);
        var list = new ListBox { ItemsSource = pages ?? ["Jobs", "Overview", "Usage", "Fleet"], SelectedIndex = 0 };
        Cursor.SetSlides(list, true);
        view.Window.Content = list;

        return (view.Settle(), list);
    }
}
