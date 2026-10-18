using Avala.Jobs.Contracts;
using Avala.Notes.Contracts;
using Avala.Sdk.Presentation;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Following;
using Avala.Workbench.Navigation;
using Avala.Workbench.Notes;
using Avala.Workbench.Projects;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Workbench.Tests.Notes;

public sealed class ProjectNotesViewModelScripts
{
    [Fact]
    public async Task AskingForAProjectsNotesShowsThePageListingThemAsync()
    {
        var messenger = new StrongReferenceMessenger();
        var shown = new List<PageRequested>();
        messenger.Register<PageRequested>(shown, (list, message) => ((List<PageRequested>)list).Add(message));
        var notes = new FakeNotes();
        notes.Add(NotePlace.OfProject("/home/ada/code/ledger-api"), "Roadmap", "Ship notes.", DateTimeOffset.UnixEpoch);
        notes.Add(NotePlace.OfJob(JobId.New(), "/home/ada/code/ledger-api"), "Login bug", "The button overlaps.", DateTimeOffset.UnixEpoch);
        using var ui = new TestUiDispatcher();
        using var page = new ProjectNotesViewModel(
            new NoteBoardViewModel(notes, new FakePictures(), new FakeTimeProvider()),
            new LiveFeed(new Pulse(new JobBoard()), ui, NullLogger<LiveFeed>.Instance),
            new JobFocus(new TestRegions(), messenger),
            messenger);

        await ui.InvokeAsync(() => messenger.Send(new ProjectNotesRequested("/home/ada/code/ledger-api")), TestContext.Current.CancellationToken);
        page.Activate();
        await ui.PresentedAsync(page, () => page.Board.Notes.Count == 1, () => $"{page.Board.Notes.Count} notes");

        Assert.Equal(("Notes", "ledger-api", "Roadmap"), await ui.ReadAsync(() => (page.Title, page.Where, page.Board.Notes[0].Title)));
        Assert.Same(page, Assert.Single(shown).Page);
    }
}
