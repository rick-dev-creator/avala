using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Workbench.Conversation;
using Avala.Workbench.Replies;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Cards;

internal interface IDeniedCardViewModel
{
    string What { get; }

    string Why { get; }

    bool IsAllowed { get; }

    bool CanAllow { get; }

    string Notice { get; }

    string Error { get; }

    IAsyncRelayCommand AllowCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class DeniedCardViewModel : IDeniedCardViewModel, ITimelineItem
{
    private readonly HumanReplies replies;
    private DeniedEntry denied;

    public DeniedCardViewModel(DeniedEntry entry, HumanReplies replies)
    {
        this.replies = replies;
        denied = entry;
        Notice = string.Empty;
        Error = string.Empty;
        Update(entry);
    }

    public bool IsShown => true;

    public string What => CardPhrases.Denied(denied.Decision.Kind, denied.Decision.Target);

    public string Why => CardPhrases.WhyDenied(denied.Decision);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAllow))]
    [NotifyCanExecuteChangedFor(nameof(AllowCommand))]
    public partial bool IsAllowed { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAllow))]
    [NotifyCanExecuteChangedFor(nameof(AllowCommand))]
    public partial bool IsAllowing { get; private set; }

    public bool CanAllow => !IsAllowed && !IsAllowing && Allowable;

    [ObservableProperty]
    public partial string Notice { get; private set; }

    [ObservableProperty]
    public partial string Error { get; private set; }

    private bool Allowable => denied.Decision.Scope.Match(scope => scope.Rule.IsSome || scope.Folder.IsSome, () => false);

    public void Update(ITimelineEntry entry)
    {
        if (entry is DeniedEntry changed)
        {
            denied = changed;
            IsAllowed = changed.Allowed.IsSome;
            Notice = IsAllowed && Notice.Length == 0 ? CardPhrases.AllowedAfterDenial(Option<JobRejection>.None) : Notice;
            OnPropertyChanged(nameof(CanAllow));
        }
    }

    [RelayCommand(CanExecute = nameof(CanAllow))]
    private async Task AllowAsync(CancellationToken cancellationToken)
    {
        IsAllowing = true;

        try
        {
            var allowed = await replies.AllowAsync(denied, cancellationToken);
            Error = allowed.Match(_ => string.Empty, CardPhrases.Error);
            Notice = allowed.Match(retry => CardPhrases.AllowedAfterDenial(retry.Untold), _ => string.Empty);
            IsAllowed = allowed.IsSuccess;
        }
        finally
        {
            IsAllowing = false;
        }
    }
}
