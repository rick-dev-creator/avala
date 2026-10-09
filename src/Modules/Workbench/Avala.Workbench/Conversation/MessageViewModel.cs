using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Conversation;

[INotifyPropertyChanged]
internal sealed partial class MessageViewModel : ITimelineItem
{
    public MessageViewModel(MessageEntry entry)
    {
        Text = string.Empty;
        Update(entry);
    }

    public bool IsShown => true;

    [ObservableProperty]
    public partial string Text { get; private set; }

    [ObservableProperty]
    public partial bool IsStreaming { get; private set; }

    public void Update(ITimelineEntry entry)
    {
        if (entry is MessageEntry message)
        {
            Text = message.Text;
            IsStreaming = message.Outcome.IsNone;
        }
    }
}
