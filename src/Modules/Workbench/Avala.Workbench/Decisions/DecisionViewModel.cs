using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
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

    string Context { get; }

    IReadOnlyList<DecisionOption> Options { get; }

    bool IsSingleChoice { get; }

    string Waiting { get; }

    bool IsSelected { get; }

    IAsyncRelayCommand AnswerCommand { get; }

    IAsyncRelayCommand DenyCommand { get; }

    IRelayCommand OpenCommand { get; }
}

internal sealed record DecisionOption(int Number, IFormChoiceViewModel Choice);

[INotifyPropertyChanged]
internal sealed partial class DecisionViewModel : IDecisionViewModel
{
    public DecisionViewModel(JobId job, string jobTitle, ITimelineItem card, DateTimeOffset since)
    {
        Job = job;
        JobTitle = jobTitle;
        Card = card;
        Since = since;
        Waiting = string.Empty;
        Note = string.Empty;
        var field = (card as IFormCardViewModel)?.Fields.FirstOrDefault(found => found.Choices.Count > 0);
        Options = field is null ? [] : [.. field.Choices.Select((choice, index) => new DecisionOption(index + 1, choice))];
        IsSingleChoice = field?.Kind != FieldKind.MultipleChoice;
        (Title, Asking, Target, Context) = card switch
        {
            IPermissionCardViewModel permission => (permission.Title, FactPhrases.Asking(permission.Kind), permission.Target, string.Empty),
            IFormCardViewModel form => (form.Title, FactPhrases.Asking(form.Purpose), string.Empty, form.Context),
            _ => (string.Empty, string.Empty, string.Empty, string.Empty),
        };
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

    public string Context { get; }

    public IReadOnlyList<DecisionOption> Options { get; }

    public bool IsSingleChoice { get; }

    public DateTimeOffset Since { get; }

    object IDecisionViewModel.Card => Card;

    [ObservableProperty]
    public partial string Waiting { get; private set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string Note { get; set; }

    public void Update(ITimelineEntry entry, DateTimeOffset now)
    {
        Card.Update(entry);
        Waiting = Waited(now - Since);
    }

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
