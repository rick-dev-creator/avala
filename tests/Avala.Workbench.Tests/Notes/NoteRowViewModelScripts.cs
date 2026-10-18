using Avala.Jobs.Contracts;
using Avala.Notes.Contracts;
using Avala.Workbench.Notes;

namespace Avala.Workbench.Tests.Notes;

public sealed class NoteRowViewModelScripts
{
    [Fact]
    public void ARowShowsWhenTheNoteChangedItsFirstWrittenLineAndHowManyImagesItHolds()
    {
        var updated = new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
        var note = new NoteRecord(
            NoteId.New(),
            NotePlace.OfJob(JobId.New(), "/repo"),
            "Login bug",
            "\n![The login screen](login.png)\n  The button overlaps.  \nOn narrow windows only.",
            [new NoteMedia("login.png", "image/png"), new NoteMedia("menu.png", "image/png")],
            NoteState.Kept,
            updated,
            updated);
        var opened = new List<NoteRecord>();

        var row = new NoteRowViewModel(note, updated.AddMinutes(12), opened.Add);
        row.OpenCommand.Execute(null);

        Assert.Equal(("Login bug", "12 min ago", "The button overlaps.", "2 images"), (row.Title, row.When, row.Excerpt, row.Images));
        Assert.Equal([note], opened);
    }
}
