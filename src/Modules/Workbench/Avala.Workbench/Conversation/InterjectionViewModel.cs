using Avala.Components.Virtualizing;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Conversation;

internal interface IInterjectionViewModel
{
    IReadOnlyList<IAttachmentChipViewModel> Attachments { get; }

    bool HasAttachments { get; }

    FindMark Found { get; }

    string Note { get; }

    string Text { get; }

    bool IsFromDevice { get; }
}

[INotifyPropertyChanged]
internal sealed partial class InterjectionViewModel(InterjectionEntry entry) : IInterjectionViewModel, ITimelineItem, ITextSized, ISearchable
{
    public string Note => IsFromDevice ? "From your phone, while it worked · joined the turn" : "You, while it worked · joined the turn";

    public bool IsFromDevice { get; } = entry.FromDevice;

    public string Text { get; } = entry.Text;

    public int TextLength => Text.Length;

    public bool IsShown => true;

    public IReadOnlyList<IAttachmentChipViewModel> Attachments { get; init; } = [];

    public bool HasAttachments => Attachments.Count > 0;

    [ObservableProperty]
    public partial FindMark Found { get; set; }

    public string SearchText => Text;

    public bool Unfold() => false;

    public void Update(ITimelineEntry entry)
    {
    }
}
