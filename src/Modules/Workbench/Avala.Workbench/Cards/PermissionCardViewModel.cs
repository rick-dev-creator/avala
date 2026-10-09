using Avala.Agents.Contracts.Events;
using Avala.Sdk;
using Avala.Workbench.Conversation;
using Avala.Workbench.Replies;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Cards;

internal interface IPermissionCardViewModel
{
    string Title { get; }

    ItemKind Kind { get; }

    string Target { get; }

    bool AwaitsYou { get; }

    string Verdict { get; }

    string Note { get; set; }

    bool DontAskAgain { get; set; }

    string Error { get; }

    IAsyncRelayCommand AllowCommand { get; }

    IAsyncRelayCommand DenyCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class PermissionCardViewModel : IPermissionCardViewModel, ITimelineItem
{
    private readonly HumanReplies replies;
    private PermissionEntry request;

    public PermissionCardViewModel(PermissionEntry entry, HumanReplies replies)
    {
        this.replies = replies;
        request = entry;
        Note = string.Empty;
        Verdict = string.Empty;
        Error = string.Empty;
        Update(entry);
    }

    public string Title => request.Title;

    public ItemKind Kind => request.Kind;

    public string Target => request.Target;

    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AllowCommand), nameof(DenyCommand))]
    public partial bool AwaitsYou { get; private set; }

    [ObservableProperty]
    public partial string Verdict { get; private set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    [ObservableProperty]
    public partial bool DontAskAgain { get; set; }

    [ObservableProperty]
    public partial string Error { get; private set; }

    public void Update(ITimelineEntry entry)
    {
        if (entry is PermissionEntry permission)
        {
            request = permission;
            IsShown = permission.WentToHuman;
            AwaitsYou = permission.AwaitsHuman;
            Verdict = CardPhrases.Verdict(permission);
        }
    }

    [RelayCommand(CanExecute = nameof(AwaitsYou))]
    private Task AllowAsync(CancellationToken cancellationToken) => AnswerAsync(PermissionAnswer.Allow, cancellationToken);

    [RelayCommand(CanExecute = nameof(AwaitsYou))]
    private Task DenyAsync(CancellationToken cancellationToken) => AnswerAsync(PermissionAnswer.Deny, cancellationToken);

    private async Task AnswerAsync(PermissionAnswer answer, CancellationToken cancellationToken)
    {
        var note = string.IsNullOrWhiteSpace(Note) ? Option<string>.None : Note.Trim();
        var answered = await replies.AnswerAsync(request, answer, note, DontAskAgain, cancellationToken);
        Error = answered.Match(_ => string.Empty, CardPhrases.Error);
    }
}
