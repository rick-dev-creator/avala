using Avala.Workbench.Linking;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Conversation;

internal interface IMessageViewModel
{
    string Text { get; }

    bool IsStreaming { get; }

    string LinkNotice { get; }

    IAsyncRelayCommand<string> OpenLinkCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class MessageViewModel : IMessageViewModel, ITimelineItem
{
    private readonly Links links;

    public MessageViewModel(MessageEntry entry, Links links)
    {
        this.links = links;
        Text = string.Empty;
        Update(entry);
    }

    public bool IsShown => true;

    [ObservableProperty]
    public partial string Text { get; private set; }

    [ObservableProperty]
    public partial bool IsStreaming { get; private set; }

    [ObservableProperty]
    public partial string LinkNotice { get; private set; } = string.Empty;

    public void Update(ITimelineEntry entry)
    {
        if (entry is MessageEntry message)
        {
            Text = message.Text;
            IsStreaming = message.Outcome.IsNone;
        }
    }

    [RelayCommand]
    private async Task OpenLinkAsync(string? link, CancellationToken cancellationToken)
    {
        var text = link ?? string.Empty;
        var opened = await links.OpenAsync(text, cancellationToken);
        LinkNotice = opened.Match(_ => string.Empty, refusal => ConversationPhrases.Link(refusal, text));
    }
}
