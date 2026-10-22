using Avala.Notes.Contracts;
using Avala.Workbench.Archive;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Notes;

internal interface INoteRowViewModel
{
    string Title { get; }

    string When { get; }

    string Excerpt { get; }

    bool HasExcerpt { get; }

    string Images { get; }

    bool HasImages { get; }

    bool IsIdea { get; }

    bool IsSelected { get; }

    IRelayCommand OpenCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class NoteRowViewModel : INoteRowViewModel
{
    private readonly Action<NoteRecord> open;

    public NoteRowViewModel(NoteRecord note, DateTimeOffset now, Action<NoteRecord> open)
    {
        this.open = open;
        Note = note;
        Title = note.Title.Trim().Length > 0 ? note.Title.Trim() : NotePhrases.Untitled;
        When = ArchivePhrases.Ended(note.Updated, now);
        Excerpt = NotePhrases.Excerpt(note.Body);
        Images = NotePhrases.Images(note.Media.Count(NoteImageViewModel.Shows));
    }

    public NoteRecord Note { get; }

    public string Title { get; }

    public string When { get; }

    public string Excerpt { get; }

    public bool HasExcerpt => Excerpt.Length > 0;

    public string Images { get; }

    public bool HasImages => Images.Length > 0;

    public bool IsIdea => Note.State == NoteState.Idea;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Open() => open(Note);
}
