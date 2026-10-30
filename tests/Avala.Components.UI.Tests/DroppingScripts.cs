using Avala.Components.UI.States;
using Avala.Testing;
using Avala.Testing.UI;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Components.UI.Tests;

public sealed class DroppingScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task DroppingFilesRunsTheCommandWithTheirPathsAsync() =>
        ui.RunAsync(async () =>
        {
            await using var folder = new TemporaryFolder();
            var dropped = new List<object?>();
            var (view, target) = Shown(dropped);
            using var files = Carrying(await StoredFiles.WrittenAsync(view.Window, folder, "notes.md", "plan.md"));
            var drop = new DragEventArgs(DragDrop.DropEvent, files, target, default, KeyModifiers.None);

            target.RaiseEvent(drop);

            Assert.True(drop.Handled);
            Assert.Equal([Path.Combine(folder.Path, "notes.md"), Path.Combine(folder.Path, "plan.md")], Assert.IsAssignableFrom<IEnumerable<string>>(Assert.Single(dropped)));
        }, Cancellation);

    [Fact]
    public Task DroppingTextIsLeftAloneAsync() =>
        ui.RunAsync(() =>
        {
            var dropped = new List<object?>();
            var (_, target) = Shown(dropped);
            using var text = new DataTransfer();
            text.Add(DataTransferItem.CreateText("Fix the calculator"));
            var drop = new DragEventArgs(DragDrop.DropEvent, text, target, default, KeyModifiers.None);

            target.RaiseEvent(drop);

            Assert.Equal((false, 0), (drop.Handled, dropped.Count));
        }, Cancellation);

    [Theory]
    [InlineData(true, DragDropEffects.Copy)]
    [InlineData(false, DragDropEffects.None)]
    public Task DraggingOverOffersToCopyOnlyFilesAsync(bool files, DragDropEffects offered) =>
        ui.RunAsync(async () =>
        {
            await using var folder = new TemporaryFolder();
            var (view, target) = Shown([]);
            using var data = files ? Carrying(await StoredFiles.WrittenAsync(view.Window, folder, "notes.md")) : new DataTransfer();
            var over = new DragEventArgs(DragDrop.DragOverEvent, data, target, default, KeyModifiers.None) { DragEffects = DragDropEffects.Copy | DragDropEffects.Move };

            target.RaiseEvent(over);

            Assert.Equal(offered, over.DragEffects);
        }, Cancellation);

    private static (ViewScript View, Border Target) Shown(List<object?> dropped)
    {
        var target = new Border();
        Dropping.SetCommand(target, new RelayCommand<object?>(dropped.Add));

        return (ViewScript.Show(target, new object()), target);
    }

    private static DataTransfer Carrying(IEnumerable<IStorageItem> files)
    {
        var data = new DataTransfer();

        foreach (var file in files)
        {
            data.Add(DataTransferItem.CreateFile(file));
        }

        return data;
    }
}
