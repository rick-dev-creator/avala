using Avala.Components.Code;
using Avala.Components.Virtualizing;
using Avala.Sdk;
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

    bool IsCopied { get; }

    IAsyncRelayCommand<string> OpenLinkCommand { get; }

    IAsyncRelayCommand<CodeRequest> CodeCommand { get; }

    IAsyncRelayCommand CopyCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class MessageViewModel : IMessageViewModel, ITimelineItem, ITextSized
{
    private readonly Links links;
    private readonly IClipboard clipboard;
    private readonly CodeFiles code;

    public MessageViewModel(MessageEntry entry, Links links, IClipboard clipboard, CodeFiles code)
    {
        this.links = links;
        this.clipboard = clipboard;
        this.code = code;
        Text = string.Empty;
        Update(entry);
    }

    public bool IsShown => true;

    public int TextLength => Text.Length;

    [ObservableProperty]
    public partial string Text { get; private set; }

    [ObservableProperty]
    public partial bool IsStreaming { get; private set; }

    [ObservableProperty]
    public partial string LinkNotice { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsCopied { get; private set; }

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

    [RelayCommand]
    private async Task CodeAsync(CodeRequest? request, CancellationToken cancellationToken)
    {
        if (request is not null)
        {
            LinkNotice = (await code.HandleAsync(request, cancellationToken)).Match(_ => string.Empty, ConversationPhrases.Code);
        }
    }

    [RelayCommand]
    private async Task CopyAsync(CancellationToken cancellationToken)
    {
        IsCopied = false;
        IsCopied = await clipboard.CopyAsync(Text, cancellationToken);
    }
}
