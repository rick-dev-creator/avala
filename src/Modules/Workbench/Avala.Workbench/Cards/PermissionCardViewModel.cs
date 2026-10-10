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

    string Note { get; set; }

    bool DontAskAgain { get; set; }

    string DontAskAgainLabel { get; }

    string DontAskAgainScope { get; }

    bool OffersAlwaysInRepository { get; }

    bool AlwaysInRepository { get; set; }

    string AlwaysInRepositoryLabel { get; }

    string AlwaysInRepositoryScope { get; }

    string Notice { get; }

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
        Notice = string.Empty;
        Error = string.Empty;
        Update(entry);
    }

    public string Title => CommandPhrases.Title(request.Kind, request.Title, request.Target);

    public ItemKind Kind => request.Kind;

    public string Headline => CardPhrases.Headline(request.Kind);

    public string Target => request.Target;

    public string Writes => CardPhrases.Writes(CommandPhrases.Writes(request.Kind, request.Target));

    public string DontAskAgainLabel => CardPhrases.DontAskAgain;

    public string DontAskAgainScope => CardPhrases.DontAskAgainScope(request.Kind);

    public bool OffersAlwaysInRepository => RepositoryRule.IsSome;

    public string AlwaysInRepositoryLabel => CardPhrases.AlwaysInRepository;

    public string AlwaysInRepositoryScope => RepositoryRule.Match(CardPhrases.AlwaysInRepositoryScope, () => string.Empty);

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
    public partial bool AlwaysInRepository { get; set; }

    [ObservableProperty]
    public partial string Notice { get; private set; }

    [ObservableProperty]
    public partial string Error { get; private set; }

    private Option<PolicyRule> RepositoryRule => request.Decision.Bind(decision => decision.RepositoryRule);

    public void Update(ITimelineEntry entry)
    {
        if (entry is PermissionEntry permission)
        {
            request = permission;
            IsShown = permission.WentToHuman;
            AwaitsYou = permission.AwaitsHuman;
            Verdict = CardPhrases.Verdict(permission);
            OnPropertyChanged(nameof(OffersAlwaysInRepository));
            OnPropertyChanged(nameof(AlwaysInRepositoryScope));
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AllowCommand), nameof(DenyCommand))]
    public partial bool IsAnswering { get; private set; }

    partial void OnDontAskAgainChanged(bool value)
    {
        if (!value)
        {
            AlwaysInRepository = false;
        }
    }

    partial void OnAlwaysInRepositoryChanged(bool value)
    {
        if (value)
        {
            DontAskAgain = true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAnswer))]
    private Task AllowAsync(CancellationToken cancellationToken) => AnswerAsync(PermissionAnswer.Allow, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanAnswer))]
    private Task DenyAsync(CancellationToken cancellationToken) => AnswerAsync(PermissionAnswer.Deny, cancellationToken);

    private bool CanAnswer() => AwaitsYou && !IsAnswering;

    private Remember Remembered =>
        AlwaysInRepository && OffersAlwaysInRepository ? Remember.InThisRepository
        : DontAskAgain ? Remember.ForThisJob
        : Remember.Once;

    private async Task AnswerAsync(PermissionAnswer answer, CancellationToken cancellationToken)
    {
        IsAnswering = true;

        try
        {
            var note = string.IsNullOrWhiteSpace(Note) ? Option<string>.None : Note.Trim();
            var answered = await replies.AnswerAsync(request, answer, note, Remembered, cancellationToken);
            Error = answered.Match(_ => string.Empty, CardPhrases.Error);
            Notice = answered.Match(CardPhrases.Remembered, _ => string.Empty);
        }
        finally
        {
            IsAnswering = false;
        }
    }
}
