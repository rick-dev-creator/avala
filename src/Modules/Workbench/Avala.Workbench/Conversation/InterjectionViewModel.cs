using Avala.Components.Virtualizing;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Conversation;

internal interface IInterjectionViewModel
{
    string Note { get; }

    string Text { get; }

    bool IsFromDevice { get; }
}

internal sealed class InterjectionViewModel(InterjectionEntry entry) : IInterjectionViewModel, ITimelineItem, ITextSized
{
    public string Note => IsFromDevice ? "From your phone, while it worked · joined the turn" : "You, while it worked · joined the turn";

    public bool IsFromDevice { get; } = entry.FromDevice;

    public string Text { get; } = entry.Text;

    public int TextLength => Text.Length;

    public bool IsShown => true;

    public void Update(ITimelineEntry entry)
    {
    }
}
