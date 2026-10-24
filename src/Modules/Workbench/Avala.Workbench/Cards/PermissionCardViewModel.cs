using Avala.Agents.Contracts.Events;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Workbench.Conversation;
using Avala.Workbench.Presenting;
using Avala.Workbench.Replies;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Cards;

internal interface IPermissionCardViewModel
{
    string Title { get; }

    ItemKind Kind { get; }

    string Headline { get; }

    string Target { get; }

    string Writes { get; }

    bool AwaitsYou { get; }

    string Verdict { get; }

    bool IsAllowed { get; }

    bool IsDenied { get; }

    string Note { get; set; }

    IAnswerScopeViewModel Scope { get; }

    string Notice { get; }

    string Error { get; }

    IAsyncRelayCommand AllowCommand { get; }

    IAsyncRelayCommand DenyCommand { get; }

    bool IsFolder { get; }

    string AllowLabel { get; }

    bool OffersReadOnly { get; }

    IAsyncRelayCommand ReadOnlyCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class PermissionCardViewModel : IPermissionCardViewModel, ITimelineItem
{
    private readonly HumanReplies replies;
    private readonly AnswerScopeViewModel scope;
    private PermissionEntry request;

    public PermissionCardViewModel(PermissionEntry entry, HumanReplies replies)
    {
        this.replies = replies;
        request = entry;
        scope = new AnswerScopeViewModel(entry.Kind, entry.Decision);
        Note = string.Empty;
        Verdict = string.Empty;
        Notice = string.Empty;
        Error = string.Empty;
        Update(entry);
    }

    public string Title => Folder.Match(CardPhrases.FolderTitle, () => CommandPhrases.Title(request.Kind, request.Title, request.Target));

    public ItemKind Kind => request.Kind;

    public string Headline => Folder.Match(CardPhrases.FolderHeadline, () => CardPhrases.Headline(request.Kind));

    public bool IsFolder => Folder.IsSome;

    public string AllowLabel => Folder.Match(CardPhrases.FolderAllow, () => CardPhrases.Allow);

    public bool OffersReadOnly => Folder.Match(found => found.Access == FolderAccess.Edit, () => false);

    private Option<FolderRequest> Folder => request.Decision.Bind(decision => decision.Folder);

    public string Target => request.Target;

    public string Writes => CardPhrases.Writes(CommandPhrases.Writes(request.Kind, request.Target));

    public IAnswerScopeViewModel Scope => scope;

    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AllowCommand), nameof(DenyCommand), nameof(ReadOnlyCommand))]
    public partial bool AwaitsYou { get; private set; }

    [ObservableProperty]
    public partial string Verdict { get; private set; }

    [ObservableProperty]
    public partial bool IsAllowed { get; private set; }

    [ObservableProperty]
    public partial bool IsDenied { get; private set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    [ObservableProperty]
    public partial string Notice { get; private set; }

    [ObservableProperty]
    public partial string Error { get; private set; }

    public void Update(ITimelineEntry entry)
    {
        if (entry is PermissionEntry permission)
        {
            request = permission;
            IsShown = permission.WentToHuman || permission.WentToParent;
            AwaitsYou = permission.AwaitsHuman || permission.AwaitsParent;
            Verdict = CardPhrases.Verdict(permission);
            IsAllowed = permission.Resolution.Match(answer => answer == PermissionAnswer.Allow, () => false);
            IsDenied = permission.Resolution.Match(answer => answer == PermissionAnswer.Deny, () => false);
            Notice = permission.Answered.Match(CardPhrases.Remembered, () => Notice);
            scope.Update(permission.Kind, permission.Decision);
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Headline));
            OnPropertyChanged(nameof(IsFolder));
            OnPropertyChanged(nameof(AllowLabel));
            OnPropertyChanged(nameof(OffersReadOnly));
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AllowCommand), nameof(DenyCommand), nameof(ReadOnlyCommand))]
    public partial bool IsAnswering { get; private set; }

    [RelayCommand(CanExecute = nameof(CanAnswer))]
    private Task AllowAsync(CancellationToken cancellationToken) =>
        AnswerAsync(PermissionAnswer.Allow, Folder.Match(found => found.Access == FolderAccess.Edit ? Remember.EditsInThisFolder : Remember.ReadsInThisFolder, () => scope.Remember), cancellationToken);

    [RelayCommand(CanExecute = nameof(CanAnswer))]
    private Task DenyAsync(CancellationToken cancellationToken) => AnswerAsync(PermissionAnswer.Deny, IsFolder ? Remember.Once : scope.Remember, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanAnswer))]
    private Task ReadOnlyAsync(CancellationToken cancellationToken) => AnswerAsync(PermissionAnswer.Allow, Remember.ReadsInThisFolder, cancellationToken);

    private bool CanAnswer() => AwaitsYou && !IsAnswering;

    private async Task AnswerAsync(PermissionAnswer answer, Remember remember, CancellationToken cancellationToken)
    {
        IsAnswering = true;

        try
        {
            var note = string.IsNullOrWhiteSpace(Note) ? Option<string>.None : Note.Trim();
            var answered = await replies.AnswerAsync(request, answer, note, remember, cancellationToken);
            Error = answered.Match(_ => string.Empty, CardPhrases.Error);
            Notice = answered.Match(CardPhrases.Remembered, _ => string.Empty);
        }
        finally
        {
            IsAnswering = false;
        }
    }
}
