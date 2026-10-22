using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Notes.Contracts;
using Avala.Workbench.Conversation;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class MediaActionsScripts
{
    private readonly FakeClipboard clipboard = new();
    private readonly FakeNotes notes = new();
    private readonly FakeCatalog catalog = new();
    private readonly JobId job;

    public MediaActionsScripts() => job = catalog.Add("Fix the login").Summary.Job;

    [Fact]
    public async Task CopyPutsTheImageOnTheClipboardAsync()
    {
        var media = Media(clipboard);

        await media.CopyCommand.ExecuteAsync(null);

        Assert.Equal(("/shots/login.png", MediaPhrases.Copied), (Assert.Single(clipboard.Images), media.Notice));
    }

    [Fact]
    public async Task CopyAsMarkdownPutsAnImageReferenceWithItsCaptionOnTheClipboardAsync()
    {
        await Media(clipboard).CopyMarkdownCommand.ExecuteAsync(null);

        Assert.Equal(["![The login screen](file:///shots/login.png)"], clipboard.Copied);
    }

    [Fact]
    public async Task CopyLinkPutsTheImagesFileAddressOnTheClipboardAsync()
    {
        await Media(clipboard).CopyLinkCommand.ExecuteAsync(null);

        Assert.Equal(["file:///shots/login.png"], clipboard.Copied);
    }

    [Fact]
    public async Task ACopyTheClipboardRefusesIsSaidSoAsync()
    {
        var media = Media(new FakeClipboard { Refuses = true });

        await media.CopyCommand.ExecuteAsync(null);

        Assert.Equal((MediaPhrases.NotCopied, true), (media.Notice, media.HasNotice));
    }

    [Fact]
    public async Task AddToNoteStartsANoteTitledByTheCaptionWhenTheJobHasNoneAsync()
    {
        var media = Media(clipboard);

        await media.AddToNoteCommand.ExecuteAsync(null);

        var kept = Assert.Single(await notes.ListAsync(Place, TestContext.Current.CancellationToken));
        Assert.Equal(("login.png", "The login screen", MediaPhrases.AddedToNote), (Assert.Single(kept.Media).File, kept.Title, media.Notice));
    }

    [Fact]
    public async Task AddToNoteAttachesTheImageToTheJobsLatestNoteAsync()
    {
        notes.Add(Place, "Older", string.Empty, DateTimeOffset.UnixEpoch.AddDays(1));
        var latest = notes.Add(Place, "Latest", string.Empty, DateTimeOffset.UnixEpoch.AddDays(2));

        await Media(clipboard).AddToNoteCommand.ExecuteAsync(null);

        Assert.Equal((1, 0), (notes.Find(latest.Id).Media.Count, (await notes.ListAsync(Place, TestContext.Current.CancellationToken)).Single(note => note.Title == "Older").Media.Count));
    }

    [Fact]
    public async Task AddToNoteSaysSoWhenTheNoteRejectsTheImageAsync()
    {
        notes.Refusal = NoteRejection.NotAnImage;
        var media = Media(clipboard);

        await media.AddToNoteCommand.ExecuteAsync(null);

        Assert.Equal(MediaPhrases.NotAddedToNote, media.Notice);
    }

    private NotePlace Place => NotePlace.OfJob(job, catalog.Summary(job).Repository);

    private MediaViewModel Media(FakeClipboard on) =>
        new(job, new MediaEntry("media", new Attachment("login.png", "image/png", "/shots/login.png"), "The login screen"), new FakePictures(), Tools.Actions(on, new MediaNoting(notes, catalog)));
}
