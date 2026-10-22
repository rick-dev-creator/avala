using Avala.Components.Pictures;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Notes;

internal sealed class DesignNotesToggleViewModel : INotesToggleViewModel
{
    public bool IsShown => true;

    public bool IsOpen { get; init; } = true;

    public IRelayCommand ToggleCommand { get; } = new RelayCommand(() => { });
}

internal sealed class DesignNoteRowViewModel(string title, string when, string excerpt, string images) : INoteRowViewModel
{
    public DesignNoteRowViewModel()
        : this("Login button overlaps the logo", "12 min ago", "On narrow windows the button sits on top of the logo.", "1 image")
    {
    }

    public string Title => title;

    public string When => when;

    public string Excerpt => excerpt;

    public bool HasExcerpt => Excerpt.Length > 0;

    public string Images => images;

    public bool HasImages => Images.Length > 0;

    public bool IsIdea { get; init; }

    public bool IsSelected { get; init; }

    public IRelayCommand OpenCommand { get; } = new RelayCommand(() => { });
}

internal sealed class DesignNoteImageViewModel : INoteImageViewModel
{
    public string Name { get; init; } = "login.png";

    public IPicture Picture => NoPicture.Instance;

    public IAsyncRelayCommand<bool> ShowCommand { get; } = new AsyncRelayCommand<bool>(_ => Task.CompletedTask);
}

internal sealed class DesignNoteBoardViewModel : INoteBoardViewModel
{
    public IReadOnlyList<INoteRowViewModel> Notes { get; init; } =
    [
        new DesignNoteRowViewModel(),
        new DesignNoteRowViewModel("Split the checkout into steps", "2 h ago", "Address, delivery, payment: one screen each.", string.Empty) { IsSelected = true, IsIdea = true },
        new DesignNoteRowViewModel("Cache the exchange rates", "Yesterday", "They change once a day; fetching them per request is slow.", string.Empty),
    ];

    public bool HasNotes => Notes.Count > 0;

    public string Empty => NotePhrases.JobEmpty;

    public string Query { get; set; } = string.Empty;

    public bool IsEditing { get; init; }

    public bool IsListing => !IsEditing;

    public bool IsNew { get; init; }

    public string Title { get; set; } = "Split the checkout into steps";

    public string Body { get; set; } = "Address, delivery, payment: one screen each.\n\nKeep the summary on the right so the total never moves.";

    public IReadOnlyList<INoteImageViewModel> Images { get; init; } = [new DesignNoteImageViewModel(), new DesignNoteImageViewModel { Name = "summary.png" }];

    public string Problem { get; init; } = string.Empty;

    public IRelayCommand NewCommand { get; } = new RelayCommand(() => { });

    public IAsyncRelayCommand SaveCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public bool CanMakeIdea => true;

    public bool CanStartJob => true;

    public string Notice => string.Empty;

    public bool HasNotice => false;

    public IAsyncRelayCommand StartJobCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand PublishIssueCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand MakeIdeaCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand ArchiveCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IRelayCommand CancelCommand { get; } = new RelayCommand(() => { });
}

internal sealed class DesignNotesPaneViewModel : INotesPaneViewModel
{
    public bool IsOpen => true;

    public INoteBoardViewModel Board { get; init; } = new DesignNoteBoardViewModel();

    public IRelayCommand CloseCommand { get; } = new RelayCommand(() => { });
}

internal sealed class DesignProjectNotesViewModel : IProjectNotesViewModel
{
    public string Title => "Notes";

    public string Where => "acme-dashboard";

    public INoteBoardViewModel Board { get; init; } = new DesignNoteBoardViewModel();
}
