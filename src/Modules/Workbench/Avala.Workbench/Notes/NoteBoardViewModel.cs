using System.Collections.ObjectModel;
using Avala.Components.Pictures;
using Avala.Notes.Contracts;
using Avala.Sdk;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Notes;

internal interface INoteBoardViewModel
{
    IReadOnlyList<INoteRowViewModel> Notes { get; }

    bool HasNotes { get; }

    string Empty { get; }

    string Query { get; set; }

    bool IsEditing { get; }

    bool IsListing { get; }

    bool IsNew { get; }

    string Title { get; set; }

    string Body { get; set; }

    IReadOnlyList<INoteImageViewModel> Images { get; }

    string Problem { get; }

    IRelayCommand NewCommand { get; }

    IAsyncRelayCommand SaveCommand { get; }

    bool CanMakeIdea { get; }

    bool CanStartJob { get; }

    string Notice { get; }

    bool HasNotice { get; }

    IAsyncRelayCommand StartJobCommand { get; }

    IAsyncRelayCommand PublishIssueCommand { get; }

    IAsyncRelayCommand MakeIdeaCommand { get; }

    IAsyncRelayCommand ArchiveCommand { get; }

    IRelayCommand CancelCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class NoteBoardViewModel(INotes notes, IIdeas ideas, IPictureDecoder pictures, TimeProvider time) : INoteBoardViewModel, IDisposable
{
    private readonly ObservableCollection<NoteRowViewModel> rows = [];
    private readonly ObservableCollection<NoteImageViewModel> images = [];
    private List<NoteRecord> kept = [];
    private IReadOnlyList<NoteRecord> placed = [];
    private IReadOnlyList<NoteRecord> matches = [];
    private string home = NotePhrases.JobEmpty;
    private Option<NoteRecord> editing;

    public IReadOnlyList<INoteRowViewModel> Notes => rows;

    public IReadOnlyList<INoteImageViewModel> Images => images;

    public Option<NotePlace> Place { get; private set; }

    public bool HasNotes => rows.Count > 0;

    public bool IsListing => !IsEditing;

    public bool IsNew => IsEditing && editing.IsNone;

    public bool CanArchive => IsEditing && editing.IsSome;

    public bool CanStartJob => IsEditing && editing.Match(note => note.State == NoteState.Idea, () => false);

    public bool HasNotice => Notice.Length > 0;

    public bool CanMakeIdea => IsEditing && editing.Match(note => note.State == NoteState.Kept, () => false);

    public string Empty
    {
        get => Query.Trim().Length > 0 ? NotePhrases.NoMatch : home;
        set
        {
            home = value;
            OnPropertyChanged();
        }
    }

    public Task Searching { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Empty))]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListing), nameof(IsNew), nameof(CanArchive), nameof(CanMakeIdea), nameof(CanStartJob))]
    [NotifyCanExecuteChangedFor(nameof(ArchiveCommand), nameof(MakeIdeaCommand), nameof(StartJobCommand), nameof(PublishIssueCommand))]
    public partial bool IsEditing { get; private set; }

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Body { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string Notice { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Problem { get; private set; } = string.Empty;

    public ValueTask<IReadOnlyList<NoteRecord>> ListAsync(NotePlace place, CancellationToken cancellationToken) => notes.ListAsync(place, cancellationToken);

    public void Show(NotePlace place, IReadOnlyList<NoteRecord> listed)
    {
        if (Place != Option<NotePlace>.Some(place))
        {
            Close();
        }

        Place = place;
        List(listed);
    }

    public void Clear()
    {
        Close();
        Place = Option<NotePlace>.None;
        List([]);
    }

    public void Dispose() => Release();

    partial void OnQueryChanged(string value) => Searching = SearchAsync(value.Trim());

    [RelayCommand]
    private void New()
    {
        Open(Option<NoteRecord>.None);
        Title = string.Empty;
        Body = string.Empty;
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        foreach (var place in Place.Match<NotePlace[]>(found => [found], () => []))
        {
            var draft = new NoteDraft(Title, Body);
            var saved = await editing.Match(
                note => notes.EditAsync(note.Id, draft, cancellationToken),
                () => notes.TakeAsync(place, draft, cancellationToken));
            Settle(saved, Keep);
        }
    }

    [RelayCommand(CanExecute = nameof(CanMakeIdea))]
    private async Task MakeIdeaAsync(CancellationToken cancellationToken)
    {
        foreach (var note in editing.Match<NoteRecord[]>(found => [found], () => []))
        {
            Settle(await notes.MakeIdeaAsync(note.Id, cancellationToken), Keep);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartJob))]
    private async Task StartJobAsync(CancellationToken cancellationToken)
    {
        foreach (var note in editing.Match<NoteRecord[]>(found => [found], () => []))
        {
            var started = await ideas.StartJobAsync(note.Id, cancellationToken);
            Notice = started.Match(_ => NotePhrases.JobStarted(note.Title), _ => string.Empty);
            Problem = started.Match(_ => string.Empty, NotePhrases.Rejection);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartJob))]
    private async Task PublishIssueAsync(CancellationToken cancellationToken)
    {
        foreach (var note in editing.Match<NoteRecord[]>(found => [found], () => []))
        {
            var published = await ideas.PublishIssueAsync(note.Id, cancellationToken);
            Notice = published.Match(link => NotePhrases.IssuePublished(link), _ => string.Empty);
            Problem = published.Match(_ => string.Empty, NotePhrases.Rejection);
        }
    }

    [RelayCommand(CanExecute = nameof(CanArchive))]
    private async Task ArchiveAsync(CancellationToken cancellationToken)
    {
        foreach (var note in editing.Match<NoteRecord[]>(found => [found], () => []))
        {
            Settle(await notes.ArchiveAsync(note.Id, cancellationToken), archived => List([.. kept.Where(other => other.Id != archived.Id)]));
        }
    }

    [RelayCommand]
    private void Cancel() => Close();

    private void Settle(Result<NoteRecord, NoteRejection> settled, Action<NoteRecord> keep)
    {
        if (settled.TryGetValue(out var note, out var rejection))
        {
            keep(note);
            Close();
        }
        else
        {
            Problem = NotePhrases.Rejection(rejection);
        }
    }

    private void Keep(NoteRecord note)
    {
        var index = kept.FindIndex(other => other.Id == note.Id);
        List(index < 0 ? [note, .. kept] : [.. kept.Select(other => other.Id == note.Id ? note : other)]);
    }

    private async Task SearchAsync(string query)
    {
        matches = query.Length == 0 ? [] : await notes.SearchAsync(query, CancellationToken.None);

        if (query == Query.Trim())
        {
            List(placed);
        }
    }

    private void List(IReadOnlyList<NoteRecord> listed)
    {
        placed = listed;
        var shown = (Query.Trim().Length > 0 ? matches : listed).Where(note => note.State != NoteState.Archived).ToList();

        if (shown.Select(Stamp).SequenceEqual(kept.Select(Stamp)))
        {
            return;
        }

        kept = shown;
        rows.Clear();
        var now = time.GetUtcNow();

        foreach (var note in shown)
        {
            rows.Add(new NoteRowViewModel(note, now, found => Open(found)) { IsSelected = editing.Match(open => open.Id == note.Id, () => false) });
        }

        OnPropertyChanged(nameof(HasNotes));
    }

    private void Open(Option<NoteRecord> note)
    {
        editing = note;
        Release();
        Problem = string.Empty;
        Notice = string.Empty;

        foreach (var found in note.Match<NoteRecord[]>(open => [open], () => []))
        {
            Title = found.Title;
            Body = found.Body;

            foreach (var media in found.Media.Where(NoteImageViewModel.Shows))
            {
                images.Add(new NoteImageViewModel(Path.Combine(found.Root, found.Folder, media.File), pictures));
            }
        }

        Select();
        IsEditing = true;
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(CanArchive));
        OnPropertyChanged(nameof(CanMakeIdea));
        OnPropertyChanged(nameof(CanStartJob));
        StartJobCommand.NotifyCanExecuteChanged();
        PublishIssueCommand.NotifyCanExecuteChanged();
        ArchiveCommand.NotifyCanExecuteChanged();
        MakeIdeaCommand.NotifyCanExecuteChanged();
    }

    private void Close()
    {
        editing = Option<NoteRecord>.None;
        Release();
        Problem = string.Empty;
        IsEditing = false;
        Select();
    }

    private void Select()
    {
        foreach (var row in rows)
        {
            row.IsSelected = editing.Match(open => open.Id == row.Note.Id, () => false);
        }
    }

    private void Release()
    {
        foreach (var image in images)
        {
            image.Dispose();
        }

        images.Clear();
    }

    private static (NoteId, DateTimeOffset, string, NoteState) Stamp(NoteRecord note) => (note.Id, note.Updated, note.Title, note.State);
}
