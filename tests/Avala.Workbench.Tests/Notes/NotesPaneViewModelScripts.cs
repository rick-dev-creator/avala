using Avala.Jobs.Contracts;
using Avala.Notes.Contracts;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Following;
using Avala.Workbench.Notes;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Workbench.Tests.Notes;

public sealed class NotesPaneViewModelScripts : IDisposable
{
    private readonly JobBoard board = new();
    private readonly TestUiDispatcher ui = new();
    private readonly StrongReferenceMessenger messenger = new();
    private readonly FakeCatalog catalog = new();
    private readonly FakeNotes notes = new();
    private readonly JobId job;
    private readonly NotePlace place;

    public NotesPaneViewModelScripts()
    {
        job = catalog.Add("Fix the login").Summary.Job;
        place = NotePlace.OfJob(job, "/repo");
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => ui.Dispose();

    [Fact]
    public async Task ThePaneListsTheJobsNotesNewestFirstWithoutTheArchivedOnesAsync()
    {
        notes.Add(place, "Login bug", "The button overlaps.\nOn narrow windows only.", DateTimeOffset.UnixEpoch.AddDays(1));
        notes.Add(place, "Old idea", "Dropped.", DateTimeOffset.UnixEpoch.AddDays(2), NoteState.Archived);
        notes.Add(place, "Plan", "Split the form.", DateTimeOffset.UnixEpoch.AddDays(3));
        notes.Add(NotePlace.OfProject("/repo"), "Roadmap", "Ship notes.", DateTimeOffset.UnixEpoch.AddDays(4));
        using var pane = Pane();

        await OpenAsync();
        await ui.PresentedAsync(pane, () => pane.Board.Notes.Count == 2, () => $"{pane.Board.Notes.Count} notes");

        Assert.Equal([("Plan", "Split the form."), ("Login bug", "The button overlaps.")], await ui.ReadAsync(() => pane.Board.Notes.Select(note => (note.Title, note.Excerpt)).ToList()));
    }

    private Task OpenAsync() => ui.InvokeAsync(() => messenger.Send(new NotesPaneRequested(job)), Cancellation).AsTask();

    private NotesPaneViewModel Pane()
    {
        var pane = new NotesPaneViewModel(
            new NoteBoardViewModel(notes, new FakePictures(), new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddDays(10))),
            new LiveFeed(new Pulse(board), ui, NullLogger<LiveFeed>.Instance),
            messenger,
            catalog);
        pane.OnRegionContextChanged(job);
        pane.Activate();

        return pane;
    }
}
