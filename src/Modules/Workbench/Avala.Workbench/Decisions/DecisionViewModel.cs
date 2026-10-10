using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Cards;
using Avala.Workbench.Conversation;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Decisions;

internal interface IDecisionViewModel
{
    JobId Job { get; }

    string JobTitle { get; }

    string Title { get; }

    string Asking { get; }

    object Card { get; }

    bool IsPermission { get; }

    string Target { get; }

    string Writes { get; }

    string Context { get; }

    IReadOnlyList<DecisionOption> Options { get; }

    IReadOnlyList<IFormFieldViewModel> Fields { get; }

    bool HasFields { get; }

    bool IsSingleChoice { get; }

    bool DontAskAgain { get; set; }

    string DontAskAgainLabel { get; }

    string DontAskAgainScope { get; }

    bool OffersAlwaysInRepository { get; }

    bool AlwaysInRepository { get; set; }

    string AlwaysInRepositoryLabel { get; }

    string AlwaysInRepositoryScope { get; }

    string Waiting { get; }

    string Heading { get; }

    bool HasHeading { get; }

    string Status { get; }

    bool IsSelected { get; }

    IAsyncRelayCommand AnswerCommand { get; }

    IAsyncRelayCommand DenyCommand { get; }

    IRelayCommand OpenCommand { get; }
}

internal sealed record DecisionOption(int Number, IFormChoiceViewModel Choice);

[INotifyPropertyChanged]
internal sealed partial class DecisionViewModel : IDecisionViewModel
{
    public DecisionViewModel(JobId job, string jobTitle, ITimelineItem card, Option<DateTimeOffset> since)
    {
        Job = job;
        JobTitle = jobTitle;
        Card = card;
        Since = since;
        Waiting = string.Empty;
        Note = string.Empty;
        var fields = (card as IFormCardViewModel)?.Fields ?? [];
        var numbered = fields is [{ Choices.Count: > 0, AcceptsText: false } only] ? only : null;
        Options = numbered is null ? [] : [.. numbered.Choices.Select((choice, index) => new DecisionOption(index + 1, choice))];
        HasFields = numbered is null && fields.Count > 0;
        IsSingleChoice = numbered?.Kind != FieldKind.MultipleChoice;
        (Title, Asking, Target, Context) = card switch
        {
            IPermissionCardViewModel permission => (permission.Title, FactPhrases.Asking(permission.Kind), permission.Target, string.Empty),
            IFormCardViewModel form => (form.Title, FactPhrases.Asking(form.Purpose), string.Empty, form.Context),
            _ => (string.Empty, string.Empty, string.Empty, string.Empty),
        };
        Writes = (card as IPermissionCardViewModel)?.Writes ?? string.Empty;
        Forward(card);
    }

    public event EventHandler? Opened;

    public JobId Job { get; }

    public string JobTitle { get; }

    public string Title { get; }

    public string Asking { get; }

    public ITimelineItem Card { get; }

    public bool IsPermission => Card is IPermissionCardViewModel;

    public string Target { get; }

    public string Writes { get; }

    public string Context { get; }

    public IReadOnlyList<DecisionOption> Options { get; }

    public IReadOnlyList<IFormFieldViewModel> Fields => HasFields && Card is IFormCardViewModel form ? form.Fields : [];

    public bool HasFields { get; }

    public bool IsSingleChoice { get; }

    public Option<DateTimeOffset> Since { get; }

    public bool DontAskAgain
    {
        get => Card is IPermissionCardViewModel { DontAskAgain: true };
        set => Remember(permission => permission.DontAskAgain != value, permission => permission.DontAskAgain = value);
    }

    public string DontAskAgainLabel => CardPhrases.DontAskAgain;

    public string DontAskAgainScope => Card is IPermissionCardViewModel permission ? permission.DontAskAgainScope : string.Empty;

    public bool OffersAlwaysInRepository => Card is IPermissionCardViewModel { OffersAlwaysInRepository: true };

    public bool AlwaysInRepository
    {
        get => Card is IPermissionCardViewModel { AlwaysInRepository: true };
        set => Remember(permission => permission.AlwaysInRepository != value, permission => permission.AlwaysInRepository = value);
    }

    public string AlwaysInRepositoryLabel => CardPhrases.AlwaysInRepository;

    public string AlwaysInRepositoryScope => Card is IPermissionCardViewModel permission ? permission.AlwaysInRepositoryScope : string.Empty;

    object IDecisionViewModel.Card => Card;

    [ObservableProperty]
    public partial string Waiting { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHeading))]
    public partial string Heading { get; set; } = string.Empty;

    public bool HasHeading => Heading.Length > 0;

    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string Note { get; set; }

    public void Update(ITimelineEntry entry, DateTimeOffset now)
    {
        Card.Update(entry);
        Status = Card switch
        {
            IPermissionCardViewModel permission => permission.Verdict,
            IFormCardViewModel form => form.Verdict,
            _ => string.Empty,
        };
        Age(now);
    }

    public void Age(DateTimeOffset now) => Waiting = Since.Match(since => Waited(now - since), () => string.Empty);

    [RelayCommand(CanExecute = nameof(CanAnswer))]
    private Task AnswerAsync() => Card switch
    {
        IPermissionCardViewModel permission => Noted(permission).AllowCommand.ExecuteAsync(null),
        IFormCardViewModel form => form.SubmitCommand.ExecuteAsync(null),
        _ => Task.CompletedTask,
    };

    [RelayCommand(CanExecute = nameof(CanDeny))]
    private Task DenyAsync() => Card switch
    {
        IPermissionCardViewModel permission => Noted(permission).DenyCommand.ExecuteAsync(null),
        IFormCardViewModel form => Noted(form).DeclineCommand.ExecuteAsync(null),
        _ => Task.CompletedTask,
    };

    [RelayCommand]
    private void Open() => Opened?.Invoke(this, EventArgs.Empty);

    private bool CanAnswer() => Card switch
    {
        IPermissionCardViewModel permission => permission.AllowCommand.CanExecute(null),
        IFormCardViewModel form => form.SubmitCommand.CanExecute(null),
        _ => false,
    };

    private bool CanDeny() => Card switch
    {
        IPermissionCardViewModel permission => permission.DenyCommand.CanExecute(null),
        IFormCardViewModel form => form.DeclineCommand.CanExecute(null),
        _ => false,
    };

    private void Remember(Func<IPermissionCardViewModel, bool> changes, Action<IPermissionCardViewModel> change)
    {
        if (Card is IPermissionCardViewModel permission && changes(permission))
        {
            change(permission);
            OnPropertyChanged(nameof(DontAskAgain));
            OnPropertyChanged(nameof(AlwaysInRepository));
        }
    }

    private IPermissionCardViewModel Noted(IPermissionCardViewModel card)
    {
        card.Note = Note;

        return card;
    }

    private IFormCardViewModel Noted(IFormCardViewModel card)
    {
        card.Note = Note;

        return card;
    }

    private void Forward(ITimelineItem card)
    {
        IEnumerable<System.Windows.Input.ICommand> commands = card switch
        {
            IPermissionCardViewModel permission => [permission.AllowCommand, permission.DenyCommand],
            IFormCardViewModel form => [form.SubmitCommand, form.DeclineCommand],
            _ => [],
        };

        foreach (var command in commands)
        {
            command.CanExecuteChanged += (_, _) =>
            {
                AnswerCommand.NotifyCanExecuteChanged();
                DenyCommand.NotifyCanExecuteChanged();
            };
        }
    }

    private static string Waited(TimeSpan waited) =>
        waited.TotalMinutes < 1 ? "<1m"
        : waited.TotalHours < 1 ? string.Create(CultureInfo.InvariantCulture, $"{(int)waited.TotalMinutes}m")
        : string.Create(CultureInfo.InvariantCulture, $"{(int)waited.TotalHours}h {waited.Minutes}m");
}
