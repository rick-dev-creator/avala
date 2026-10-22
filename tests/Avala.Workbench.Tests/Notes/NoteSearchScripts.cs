using Avala.Jobs.Contracts;
using Avala.Notes.Contracts;
using Avala.Workbench.Notes;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Workbench.Tests.Notes;

public sealed class NoteSearchScripts
{
    private readonly FakeNotes notes = new();
    private readonly NotePlace here = NotePlace.OfJob(JobId.New(), "/repo");

    [Fact]
    public async Task SearchingListsMatchingNotesFromEveryJobAndProjectInsteadOfThisJobsAsync()
    {
        notes.Add(here, "Login bug", "The button overlaps.", DateTimeOffset.UnixEpoch);
        notes.Add(NotePlace.OfJob(JobId.New(), "/repo"), "Rounding plan", "Round once, at the end.", DateTimeOffset.UnixEpoch.AddDays(1));
        notes.Add(NotePlace.OfProject("/other"), "Rounding idea", "Use banker's rounding.", DateTimeOffset.UnixEpoch.AddDays(2));
        using var board = await BoardAsync();

        board.Query = "rounding";
        await board.Searching;

        Assert.Equal(["Rounding idea", "Rounding plan"], board.Notes.Select(note => note.Title));
    }

    [Fact]
    public async Task ClearingTheSearchReturnsToThisJobsNotesAsync()
    {
        notes.Add(here, "Login bug", "The button overlaps.", DateTimeOffset.UnixEpoch);
        notes.Add(NotePlace.OfProject("/other"), "Rounding idea", "Use banker's rounding.", DateTimeOffset.UnixEpoch.AddDays(2));
        using var board = await BoardAsync();
        board.Query = "rounding";
        await board.Searching;

        board.Query = string.Empty;
        await board.Searching;

        Assert.Equal(["Login bug"], board.Notes.Select(note => note.Title));
    }

    [Fact]
    public async Task ASearchWithNoMatchSaysSoAsync()
    {
        using var board = await BoardAsync();

        board.Query = "nothing";
        await board.Searching;

        Assert.Equal((false, NotePhrases.NoMatch), (board.HasNotes, board.Empty));
    }

    private async Task<NoteBoardViewModel> BoardAsync()
    {
        var board = new NoteBoardViewModel(notes, new FakeIdeas(), new FakePictures(), new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddDays(10)));
        board.Show(here, await notes.ListAsync(here, TestContext.Current.CancellationToken));

        return board;
    }
}
