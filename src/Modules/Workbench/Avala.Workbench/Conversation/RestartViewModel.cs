using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Conversation;

internal interface IRestartViewModel
{
    string Note { get; }

    string Lost { get; }

    bool HasLost { get; }
}

[INotifyPropertyChanged]
internal sealed partial class RestartViewModel(RestartEntry restart) : IRestartViewModel, ITimelineItem, ISearchable
{
    public const string KeptNote = "Avala restarted. Everything above happened before the restart.";

    public const string SummarizedNote = "Avala restarted. This job ran before conversations were kept, so its work before this point is summarized by its attempts above.";

    public string Note { get; } = restart.Kept ? KeptNote : SummarizedNote;

    public string Lost { get; } = ConversationPhrases.Lost("Avala restarted", restart.Lost);

    public bool HasLost => Lost.Length > 0;

    public string SearchText => HasLost ? $"{Note}\n{Lost}" : Note;

    [ObservableProperty]
    public partial FindMark Found { get; set; }

    public bool Unfold() => false;

    public bool IsShown => true;

    public void Update(ITimelineEntry entry)
    {
    }
}
