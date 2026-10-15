using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Sessions;
using Avala.Components.Attaching;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Conversation;

namespace Avala.Workbench.Tests.Conversation;

public sealed class AttachmentTrayViewModelScripts : IDisposable
{
    private readonly FakeJobAttachments store = new();
    private readonly FakeFilePicker picker = new();
    private readonly FakePictures pictures = new();
    private readonly AttachmentTrayViewModel tray;

    public AttachmentTrayViewModelScripts()
    {
        tray = new AttachmentTrayViewModel(store, picker, pictures);
        tray.Accept(new AcceptsAttachments(["image/png", "application/pdf", "text/*"]));
    }

    [Fact]
    public async Task PickedFilesBecomeRemovableChipsAboveTheDraftAsync()
    {
        picker.Picked.AddRange(["/home/ana/screen.png", "/home/ana/spec.pdf"]);

        await tray.AttachCommand.ExecuteAsync(null);
        tray.Chips[0].RemoveCommand.Execute(null);

        Assert.Equal(["spec.pdf"], tray.Chips.Select(chip => chip.Name));
        Assert.Equal(["spec.pdf"], tray.Attached.Select(attached => attached.Name));
        Assert.True(tray.HasAttachments);
        Assert.Equal(["screen.png", "spec.pdf"], store.Kept);
    }

    [Fact]
    public async Task DroppingOrPastingFilesAttachesThemTheSameWayAsync()
    {
        await tray.DropCommand.ExecuteAsync(["/home/ana/build.log"]);
        await tray.PasteCommand.ExecuteAsync(new Pasted(["/home/ana/notes.md"], Option<byte[]>.None, string.Empty));

        Assert.Equal(["build.log", "notes.md"], tray.Chips.Select(chip => chip.Name));
    }

    [Fact]
    public async Task APastedImageOrAVeryLongTextBecomesAnAttachmentOfItsOwnAsync()
    {
        await tray.PasteCommand.ExecuteAsync(new Pasted([], new byte[] { 137, 80, 78, 71 }, string.Empty));
        await tray.PasteCommand.ExecuteAsync(new Pasted([], Option<byte[]>.None, new string('x', Pasted.LongText + 1)));

        Assert.Equal([AttachmentTrayViewModel.PastedImage, AttachmentTrayViewModel.PastedText], tray.Chips.Select(chip => chip.Name));
        Assert.Equal([true, false], tray.Chips.Select(chip => chip.IsImage));
    }

    [Fact]
    public async Task AFileTheConnectionDoesNotTakeIsRefusedWithTheReasonAndNeverCopiedAsync()
    {
        await tray.DropCommand.ExecuteAsync(["/home/ana/archive.zip"]);

        Assert.Equal(("This connection does not take archive.zip.", false), (tray.Problem, tray.HasAttachments));
        Assert.Empty(store.Kept);
    }

    [Fact]
    public async Task AFileThatCannotBeKeptSaysWhyAsync()
    {
        store.Refusal = AttachmentRefusal.TooLarge;

        await tray.DropCommand.ExecuteAsync(["/home/ana/screen.png"]);

        Assert.Equal("screen.png is larger than 20 MB.", tray.Problem);
    }

    [Fact]
    public void AConnectionThatTakesNoAttachmentsOffersNoAttachButton()
    {
        tray.Accept(Option<AcceptsAttachments>.None);

        Assert.False(tray.CanAttach);
    }

    [Fact]
    public async Task ClearingTheTrayAfterSendingReleasesEveryChipAsync()
    {
        await tray.DropCommand.ExecuteAsync(["/home/ana/screen.png"]);
        await tray.Chips[0].ShowCommand.ExecuteAsync(true);

        tray.Clear();

        Assert.Empty(tray.Chips);
        Assert.Empty(pictures.Held);
    }

    public void Dispose() => tray.Dispose();
}

public sealed class AttachmentChipViewModelScripts
{
    [Fact]
    public async Task AnImageChipDecodesItsThumbnailOnlyWhileShownAsync()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "screen.png");
        await File.WriteAllBytesAsync(path, [137, 80, 78, 71], TestContext.Current.CancellationToken);
        var pictures = new FakePictures();
        using var chip = new AttachmentChipViewModel(new Attachment("screen.png", "image/png", path), pictures, removable: false);

        await chip.ShowCommand.ExecuteAsync(true);
        var shown = pictures.Held.ToList();
        await chip.ShowCommand.ExecuteAsync(false);

        Assert.Equal([AttachmentChipViewModel.ThumbnailWidth], shown);
        Assert.Empty(pictures.Held);
        Assert.Equal(("PNG", true, false), (chip.Kind, chip.IsImage, chip.CanRemove));
    }

    [Fact]
    public async Task AFileChipShowsItsKindAndDecodesNothingAsync()
    {
        var pictures = new FakePictures();
        using var chip = new AttachmentChipViewModel(new Attachment("build.log", "text/plain", "/nowhere/build.log"), pictures, removable: true);

        await chip.ShowCommand.ExecuteAsync(true);

        Assert.Equal(("LOG", false), (chip.Kind, chip.IsImage));
        Assert.Empty(pictures.Decoded);
    }
}
