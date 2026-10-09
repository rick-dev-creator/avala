using Avala.Workbench.Timeline;

namespace Avala.Workbench.Conversation;

internal interface IRestartViewModel
{
    string Note { get; }
}

internal sealed class RestartViewModel(RestartEntry restart) : IRestartViewModel, ITimelineItem
{
    public const string KeptNote = "Avala restarted. Everything above happened before the restart.";

    public const string SummarizedNote = "Avala restarted. This job ran before conversations were kept, so its work before this point is summarized by its attempts above.";

    public string Note { get; } = restart.Kept ? KeptNote : SummarizedNote;

    public bool IsShown => true;

    public void Update(ITimelineEntry entry)
    {
    }
}
