using Avala.Notes.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Workbench.Following;
using Avala.Workbench.Navigation;
using Avala.Workbench.Projects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Notes;

internal interface IProjectNotesViewModel
{
    string Title { get; }

    string Where { get; }

    INoteBoardViewModel Board { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ProjectNotesViewModel : IProjectNotesViewModel, IPage, IActivatable, IPresentation, IRecipient<ProjectNotesRequested>, IDisposable
{
    private readonly NoteBoardViewModel board;
    private readonly LiveFeed feed;
    private readonly JobFocus focus;
    private Option<NotePlace> place;

    public ProjectNotesViewModel(NoteBoardViewModel board, LiveFeed feed, JobFocus focus, IMessenger messenger)
    {
        this.board = board;
        this.feed = feed;
        this.focus = focus;
        board.Empty = NotePhrases.ProjectEmpty;
        messenger.Register(this);
    }

    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    public long Revision => feed.Revision;

    public Task Following => feed.Following;

    public string Title => "Notes";

    public PagePlacement Placement => PagePlacement.Hidden;

    public INoteBoardViewModel Board => board;

    [ObservableProperty]
    public partial string Where { get; private set; } = string.Empty;

    public void Activate() => feed.Start(ReadAsync, Show);

    public void Deactivate() => feed.Stop();

    public void Dispose()
    {
        feed.Dispose();
        board.Dispose();
    }

    public void Receive(ProjectNotesRequested message)
    {
        var requested = NotePlace.OfProject(message.Repository);

        if (place != Option<NotePlace>.Some(requested))
        {
            board.Clear();
        }

        place = requested;
        Where = Path.GetFileName(message.Repository.TrimEnd('/', '\\'));
        feed.Refresh();
        focus.Show(this);
    }

    private async ValueTask<PlacedNotes> ReadAsync(CancellationToken cancellationToken) =>
        await place.Match(
            async found => new PlacedNotes(found, await board.ListAsync(found, cancellationToken)),
            () => Task.FromResult(new PlacedNotes(Option<NotePlace>.None, [])));

    private void Show(PlacedNotes read)
    {
        foreach (var found in read.Place.Match<NotePlace[]>(shown => [shown], () => []).Where(found => place == Option<NotePlace>.Some(found)))
        {
            board.Show(found, read.Notes);
        }
    }
}
