using Avala.Components.UI.Markdown;
using Avala.Components.UI.States;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;

namespace Avala.Components.UI.Tests;

public sealed class DraggingScripts(HeadlessUi ui) : IDisposable
{
    private readonly Func<PointerPressedEventArgs, DataTransfer, Task> before = Dragging.Starter;

    public void Dispose() => Dragging.Starter = before;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AnImageDraggedPastTheThresholdLeavesAsTheFileAndItsAddressAsync() =>
        ui.RunAsync(async () =>
        {
            var square = new Border { Width = 100, Height = 100, Background = Avalonia.Media.Brushes.Gray };
            Dragging.SetFile(square, "/shots/login.png");

            var dragged = await DragStartedAsync(square, new Point(50, 50), new Point(90, 90), RawInputModifiers.None).WaitAsync(Cancellation);

            Assert.Equal("file:///shots/login.png", dragged.TryGetText());
        }, Cancellation);

    [Fact]
    public Task ALinkDraggedPastTheThresholdLeavesAsItsAddressAsync() =>
        ui.RunAsync(async () =>
        {
            var square = new Border { Width = 100, Height = 100, Background = Avalonia.Media.Brushes.Gray };
            Dragging.SetLink(square, "https://ci.example.com/runs/1/log");

            var dragged = await DragStartedAsync(square, new Point(50, 50), new Point(90, 90), RawInputModifiers.None).WaitAsync(Cancellation);

            Assert.Equal("https://ci.example.com/runs/1/log", dragged.TryGetText());
        }, Cancellation);

    [Fact]
    public Task AMovementShorterThanTheThresholdStartsNoDragAsync() =>
        ui.RunAsync(() =>
        {
            var square = new Border { Width = 100, Height = 100, Background = Avalonia.Media.Brushes.Gray };
            Dragging.SetText(square, "words");

            var dragged = DragStartedAsync(square, new Point(50, 50), new Point(52, 51), RawInputModifiers.None);

            Assert.False(dragged.IsCompleted);
        }, Cancellation);

    [Fact]
    public Task AnAltDragOfSelectedMarkdownTextLeavesAsThatTextAsync() =>
        ui.RunAsync(async () =>
        {
            var text = new MarkdownText { Markdown = "Rounding is applied once.", Width = 300, Height = 60 };
            var view = ViewScript.Show(text, new object());
            view.Settle();
            text.SelectAll();

            var dragged = await DragStartedAsync(text, view, new Point(20, 10), new Point(80, 40), RawInputModifiers.Alt).WaitAsync(Cancellation);

            Assert.Equal(text.SelectedText, dragged.TryGetText());
            Assert.NotEqual(string.Empty, text.SelectedText);
        }, Cancellation);

    [Fact]
    public Task ADragOfSelectedMarkdownTextWithoutAltLeavesTheSelectionToTheTextAsync() =>
        ui.RunAsync(() =>
        {
            var text = new MarkdownText { Markdown = "Rounding is applied once.", Width = 300, Height = 60 };
            var view = ViewScript.Show(text, new object());
            view.Settle();
            text.SelectAll();

            var dragged = DragStartedAsync(text, view, new Point(20, 10), new Point(80, 40), RawInputModifiers.None);

            Assert.False(dragged.IsCompleted);
        }, Cancellation);

    private static Task<IDataTransfer> DragStartedAsync(Control control, Point from, Point to, RawInputModifiers modifiers) =>
        DragStartedAsync(control, ViewScript.Show(control, new object()), from, to, modifiers);

    private static Task<IDataTransfer> DragStartedAsync(Control control, ViewScript view, Point from, Point to, RawInputModifiers modifiers)
    {
        view.Settle();
        var started = new TaskCompletionSource<IDataTransfer>();
        Dragging.Starter = (_, transfer) =>
        {
            started.TrySetResult(transfer);

            return Task.CompletedTask;
        };
        var origin = control.TranslatePoint(from, view.Window) ?? from;
        var target = control.TranslatePoint(to, view.Window) ?? to;
        view.Window.MouseDown(origin, MouseButton.Left, modifiers);
        view.Window.MouseMove(target, modifiers | RawInputModifiers.LeftMouseButton);
        view.Window.MouseUp(target, MouseButton.Left, modifiers);
        view.Settle();

        return started.Task;
    }
}
