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

    bool IsEditing { get; }

    bool IsListing { get; }

    bool IsNew { get; }

    string Title { get; set; }

    string Body { get; set; }

    IReadOnlyList<INoteImageViewModel> Images { get; }

    string Problem { get; }

    IRelayCommand NewCommand { get; }

    IAsyncRelayCommand SaveCommand { get; }

    IAsyncRelayCommand ArchiveCommand { get; }

    IRelayCommand CancelCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class NoteBoardViewModel(INotes notes, IPictureDecoder pictures, TimeProvider time) : INoteBoardViewModel, IDisposable
{
    private readonly ObservableCollection<NoteRowViewModel> rows = [];
    private readonly ObservableCollection<NoteImageViewModel> images = [];
    private List<NoteRecord> kept = [];
    private Option<NoteRecord> editing;

    public IReadOnlyList<INoteRowViewModel> Notes => rows;

    public IReadOnlyList<INoteImageViewModel> Images => images;

    public Option<NotePlace> Place { get; private set; }

    public bool HasNotes => rows.Count > 0;

    public bool IsListing => !IsEditing;

    public bool IsNew => IsEditing && editing.IsNone;

    public bool CanArchive => IsEditing && editing.IsSome;

    [ObservableProperty]
    public partial string Empty { get; set; } = NotePhrases.JobEmpty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListing), nameof(IsNew), nameof(CanArchive))]
    [NotifyCanExecuteChangedFor(nameof(ArchiveCommand))]
    public partial bool IsEditing { get; private set; }

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Body { get; set; } = string.Empty;

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

    private void List(IReadOnlyList<NoteRecord> listed)
    {
        var shown = listed.Where(note => note.State != NoteState.Archived).ToList();

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
        ArchiveCommand.NotifyCanExecuteChanged();
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

    private static (NoteId, DateTimeOffset, string) Stamp(NoteRecord note) => (note.Id, note.Updated, note.Title);
}
