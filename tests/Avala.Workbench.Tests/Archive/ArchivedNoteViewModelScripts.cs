using Avala.Jobs.Contracts;
using Avala.Workbench.Archive;

namespace Avala.Workbench.Tests.Archive;

public sealed class ArchivedNoteViewModelScripts : IDisposable
{
    private readonly Archives archives = new();

    [Fact]
    public async Task TheNoteShowsOnlyWhileTheConversationIsArchivedAsync()
    {
        var merged = archives.Job("Paginate the export", JobStatus.Approved, Archives.Ledger, TimeSpan.FromDays(3));
        using var note = Note(merged.Job);
        await archives.Ui.PresentedAsync(note, () => note.Revision > 0, () => note.IsShown.ToString());
        var before = note.IsShown;

        archives.Archive(merged);
        await archives.Ui.PresentedAsync(note, () => note.IsShown, () => note.IsShown.ToString());

        Assert.Equal((false, true), (before, note.IsShown));
    }

    [Fact]
    public async Task RestoringFromTheNoteRestoresTheConversationAsync()
    {
        var merged = archives.Job("Paginate the export", JobStatus.Approved, Archives.Ledger, TimeSpan.FromDays(3));
        archives.Archive(merged);
        using var note = Note(merged.Job);
        await archives.Ui.PresentedAsync(note, () => note.IsShown, () => note.IsShown.ToString());

        await note.RestoreCommand.ExecuteAsync(null);

        Assert.Equal(["restore"], archives.Bench.ProjectBook.Calls);
        Assert.DoesNotContain(merged.Job, archives.Bench.ProjectBook.Current.Archived);
    }

    public void Dispose() => archives.Dispose();

    private ArchivedNoteViewModel Note(JobId job)
    {
        var note = new ArchivedNoteViewModel(archives.Reader, archives.Feed(), archives.Bench.Shelf);
        note.Activate();
        note.OnRegionContextChanged(job);

        return note;
    }
}
