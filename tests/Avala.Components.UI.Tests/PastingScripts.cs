using Avala.Components.Attaching;
using Avala.Components.UI.States;
using Avala.Testing;
using Avala.Testing.UI;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Components.UI.Tests;

public sealed class PastingScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task PastingFilesHandsTheirPathsToTheCommandAsync() =>
        ui.RunAsync(async () =>
        {
            await using var folder = new TemporaryFolder();
            var pasted = new List<object?>();
            var (view, box) = Shown(pasted);
            await view.Window.Clipboard!.SetFilesAsync(await StoredFiles.WrittenAsync(view.Window, folder, "notes.md"));

            box.Paste();
            view.Settle();

            var files = Assert.IsType<Pasted>(Assert.Single(pasted));
            Assert.Equal([Path.Combine(folder.Path, "notes.md")], files.Files);
            Assert.Equal((true, string.Empty, string.Empty), (files.Image.IsNone, files.Text, box.Text ?? string.Empty));
        }, Cancellation);

    [Fact]
    public Task PastingALongTextHandsItToTheCommandInsteadOfTypingItAsync() =>
        ui.RunAsync(async () =>
        {
            var pasted = new List<object?>();
            var (view, box) = Shown(pasted);
            var log = new string('x', Pasted.LongText + 1);
            await view.Window.Clipboard!.SetTextAsync(log);

            box.Paste();
            view.Settle();

            var text = Assert.IsType<Pasted>(Assert.Single(pasted));
            Assert.Equal((0, true, log, string.Empty), (text.Files.Count, text.Image.IsNone, text.Text, box.Text ?? string.Empty));
        }, Cancellation);

    [Fact]
    public Task PastingAShortTextTypesItAtTheCaretAsync() =>
        ui.RunAsync(async () =>
        {
            var pasted = new List<object?>();
            var (view, box) = Shown(pasted);
            box.Text = "Fix the ";
            box.CaretIndex = box.Text.Length;
            await view.Window.Clipboard!.SetTextAsync("calculator");

            box.Paste();
            view.Settle();

            Assert.Equal(("Fix the calculator", 0), (box.Text, pasted.Count));
        }, Cancellation);

    private static (ViewScript View, TextBox Box) Shown(List<object?> pasted)
    {
        var box = new TextBox();
        Pasting.SetCommand(box, new RelayCommand<object?>(pasted.Add));

        return (ViewScript.Show(box, new object()), box);
    }
}
