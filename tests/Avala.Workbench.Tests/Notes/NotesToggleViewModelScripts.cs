using Avala.Jobs.Contracts;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Following;
using Avala.Workbench.Notes;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Workbench.Tests.Notes;

public sealed class NotesToggleViewModelScripts
{
    [Fact]
    public void TheGlyphOpensTheShownJobsNotesPaneAndClosesItAgain()
    {
        var messenger = new StrongReferenceMessenger();
        var catalog = new FakeCatalog();
        var job = catalog.Add("Fix the login").Summary.Job;
        using var ui = new TestUiDispatcher();
        using var toggle = new NotesToggleViewModel(messenger);
        using var pane = new NotesPaneViewModel(
            new NoteBoardViewModel(new FakeNotes(), new FakePictures(), new FakeTimeProvider()),
            new LiveFeed(new Pulse(new JobBoard()), ui, NullLogger<LiveFeed>.Instance),
            messenger,
            catalog);
        toggle.OnRegionContextChanged(job);
        pane.OnRegionContextChanged(job);

        toggle.ToggleCommand.Execute(null);
        var opened = (toggle.IsShown, toggle.IsOpen, pane.IsOpen);
        toggle.ToggleCommand.Execute(null);

        Assert.Equal((true, true, true), opened);
        Assert.Equal((false, false), (toggle.IsOpen, pane.IsOpen));
    }
}
