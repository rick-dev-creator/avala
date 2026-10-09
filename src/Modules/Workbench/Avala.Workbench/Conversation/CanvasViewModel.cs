using Avala.Canvas.Contracts;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Conversation;

[INotifyPropertyChanged]
internal sealed partial class CanvasViewModel : ITimelineItem
{
    public CanvasViewModel(CanvasEntry entry)
    {
        Title = string.Empty;
        MediaType = string.Empty;
        Content = string.Empty;
        Update(entry);
    }

    public bool IsShown => true;

    [ObservableProperty]
    public partial string Title { get; private set; }

    [ObservableProperty]
    public partial string MediaType { get; private set; }

    [ObservableProperty]
    public partial string Content { get; private set; }

    [ObservableProperty]
    public partial CanvasStatus Status { get; private set; }

    public bool IsStreaming => Status == CanvasStatus.Streaming;

    public void Update(ITimelineEntry entry)
    {
        if (entry is CanvasEntry canvas)
        {
            Title = canvas.Title;
            MediaType = canvas.MediaType;
            Content = canvas.Content;
            Status = canvas.Status;
            OnPropertyChanged(nameof(IsStreaming));
        }
    }
}
