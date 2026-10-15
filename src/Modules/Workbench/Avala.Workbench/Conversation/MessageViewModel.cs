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
    FindMark Found { get; }

    string Text { get; }

    bool IsStreaming { get; }

    string LinkNotice { get; }

    bool IsCopied { get; }

    IAsyncRelayCommand<string> OpenLinkCommand { get; }

    IAsyncRelayCommand<CodeRequest> CodeCommand { get; }

    ICodePeekViewModel Peek { get; }

    IAsyncRelayCommand CopyCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class MessageViewModel : IMessageViewModel, ITimelineItem, ITextSized, ISearchable
{
    private readonly IClipboard clipboard;
    private readonly FileViews files;
    private readonly CodePeekViewModel peek;

    public MessageViewModel(MessageEntry entry, IClipboard clipboard, FileViews files)
    {
        this.clipboard = clipboard;
        this.files = files;
        peek = files.Peek();
        Text = string.Empty;
        Update(entry);
    }

    public bool IsShown => true;

    public ICodePeekViewModel Peek => peek;

    public int TextLength => Text.Length;

    [ObservableProperty]
    public partial string Text { get; private set; }

    [ObservableProperty]
    public partial bool IsStreaming { get; private set; }

    [ObservableProperty]
    public partial string LinkNotice { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsCopied { get; private set; }

    [ObservableProperty]
    public partial FindMark Found { get; set; }

    public string SearchText => Text;

    public bool Unfold() => false;

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
        LinkNotice = await Reference.Parse(text).Match(
            async reference => reference.Kind == ReferenceKind.File
                ? await peek.ShowAsync(reference.Target, reference.Line, cancellationToken)
                : (await files.OpenAsync(reference, cancellationToken)).Match(_ => string.Empty, ConversationPhrases.Reference),
            async () => (await files.Links.OpenAsync(text, cancellationToken)).Match(_ => string.Empty, refusal => ConversationPhrases.Link(refusal, text)));
    }

    [RelayCommand]
    private async Task CodeAsync(CodeRequest? request, CancellationToken cancellationToken)
    {
        if (request is not null)
        {
            LinkNotice = (await files.Code.HandleAsync(request, cancellationToken)).Match(_ => string.Empty, ConversationPhrases.Code);
        }
    }

    [RelayCommand]
    private async Task CopyAsync(CancellationToken cancellationToken)
    {
        IsCopied = false;
        IsCopied = await clipboard.CopyAsync(Text, cancellationToken);
    }
}
