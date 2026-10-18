using Avala.Jobs.Contracts;
using Avala.Notes.Contracts;
using Avala.Notes.Notebook;
using Avala.Testing;

namespace Avala.Notes.Tests.Notebook;

public sealed class NoteTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 19, 14, 32, 0, TimeSpan.Zero);

    private static readonly NotePlace Place = NotePlace.OfJob(new JobId(Guid.CreateVersion7()), "/repo");

    [Fact]
    public void ANoteWithoutATitleIsRefused() =>
        Assert.Equal(NoteError.UntitledNote, Outcomes.FailsWith(Note.Taken(NoteId.New(), Place, new NoteDraft("  ", "body"), At)));

    [Fact]
    public void AnArchivedNoteRefusesEdits()
    {
        var archived = Outcomes.Succeeds(Note.Archived(Outcomes.Succeeds(Note.Taken(NoteId.New(), Place, new NoteDraft("Login bug", string.Empty), At)), At));

        Assert.Equal(NoteError.Archived, Outcomes.FailsWith(Note.Edited(archived, new NoteDraft("Login bug", "more"), At)));
    }

    [Fact]
    public void AttachedMediaIsReferencedFromTheBodyByItsRelativePath()
    {
        var note = Outcomes.Succeeds(Note.Taken(NoteId.New(), Place, new NoteDraft("Login bug", "The button overlaps."), At));

        var attached = Outcomes.Succeeds(Note.Attached(note, new NoteMedia("login screen.png", "image/png"), "The login screen", At));

        Assert.Equal("The button overlaps.\n\n![The login screen](login%20screen.png)", attached.Body);
        Assert.Equal(["login screen.png"], attached.Media.Select(media => media.File));
    }

    [Fact]
    public void ANotesFolderIsNamedByWhenItWasTakenAndItsTitle() =>
        Assert.Equal("2026-10-19-1432-el-boton-de-login-se-solapa", NoteFolder.Named(At, "¡El botón de login se solapa!"));

    [Fact]
    public void AJobsNotesLiveInTheJobsFolderAndOthersInTheProjects()
    {
        var job = new JobId(Guid.Parse("0199f0a4-0000-7000-8000-000000000001"));

        Assert.Equal(
            (".avala/notes/0199f0a4000070008000000000000001", ".avala/notes/project"),
            (NoteFolder.Of(NotePlace.OfJob(job, "/repo")), NoteFolder.Of(NotePlace.OfProject("/repo"))));
    }

    [Fact]
    public void ANoteReadsBackAsItWasWritten()
    {
        var note = Outcomes.Succeeds(Note.Taken(NoteId.New(), Place, new NoteDraft("Login bug", "The button overlaps.\n\n- check mobile"), At));

        var read = Outcomes.Present(NoteDocument.Read(NoteDocument.Written(note), "/repo", note.Media));

        Assert.Equal(note with { Media = read.Media }, read);
    }
}
