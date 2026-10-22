using Avala.Agents.Contracts.Sessions;
using Avala.Components.Virtualizing;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Conversation;

internal interface IPromptViewModel
{
    IReadOnlyList<IAttachmentChipViewModel> Attachments { get; }

    bool HasAttachments { get; }

    FindMark Found { get; }

    int Attempt { get; }

    string Origin { get; }

    bool IsFromPerson { get; }

    bool ShowsOrigin { get; }

    string Text { get; }

    string Outcome { get; }

    bool IsCopied { get; }

    bool CanResend { get; }

    bool CanEdit { get; }

    IAsyncRelayCommand CopyCommand { get; }

    IRelayCommand EditCommand { get; }

    IRelayCommand RetryCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class PromptViewModel : IPromptViewModel, ITimelineItem, ITextSized, ISearchable
{
    private readonly IClipboard clipboard;

    private readonly Func<IReadOnlyList<Attachment>, IReadOnlyList<IAttachmentChipViewModel>> chips;
    private IReadOnlyList<Attachment> attached = [];

    public PromptViewModel(PromptEntry entry, IClipboard clipboard)
        : this(entry, clipboard, _ => [])
    {
    }

    public PromptViewModel(PromptEntry entry, IClipboard clipboard, Func<IReadOnlyList<Attachment>, IReadOnlyList<IAttachmentChipViewModel>> chips)
    {
        this.clipboard = clipboard;
        this.chips = chips;
        Attempt = entry.Attempt;
        Origin = ConversationPhrases.Origin(entry);
        IsFromPerson = entry.IsFromPerson;
        ShowsOrigin = entry.Origin != AttemptOrigin.Initial;
        Text = entry.Text.Match(text => text, () => string.Empty);
        Outcome = string.Empty;
        Update(entry);
    }

    public int Attempt { get; }

    [ObservableProperty]
    public partial string Origin { get; private set; }

    public bool IsFromPerson { get; }

    public bool ShowsOrigin { get; }

    public string Text { get; }

    public int TextLength => Text.Length;

    public bool IsShown => true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAttachments))]
    public partial IReadOnlyList<IAttachmentChipViewModel> Attachments { get; private set; } = [];

    public bool HasAttachments => Attachments.Count > 0;

    [ObservableProperty]
    public partial string Outcome { get; private set; }

    [ObservableProperty]
    public partial bool IsCopied { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    public partial bool CanResend { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    public partial bool CanEdit { get; set; }

    public event EventHandler<string>? EditRequested;

    public event EventHandler<string>? RetryRequested;

    [ObservableProperty]
    public partial FindMark Found { get; set; }

    public string SearchText => Text;

    public bool Unfold() => false;

    public void Update(ITimelineEntry entry)
    {
        if (entry is PromptEntry prompt)
        {
            Outcome = prompt.Outcome.Match(outcome => ConversationPhrases.Outcome(prompt.Attempt, outcome), () => string.Empty);
            Origin = ConversationPhrases.Origin(prompt);

            if (!prompt.Attachments.SequenceEqual(attached))
            {
                attached = prompt.Attachments;
                Attachments = chips(attached);
            }
        }
    }

    [RelayCommand]
    private async Task CopyAsync(CancellationToken cancellationToken)
    {
        IsCopied = false;
        IsCopied = await clipboard.CopyAsync(Text, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Edit() => EditRequested?.Invoke(this, Text);

    [RelayCommand(CanExecute = nameof(CanResend))]
    private void Retry() => RetryRequested?.Invoke(this, Text);
}
