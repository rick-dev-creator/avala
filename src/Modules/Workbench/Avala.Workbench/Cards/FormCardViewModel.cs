using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Workbench.Conversation;
using Avala.Workbench.Replies;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Cards;

internal interface IFormCardViewModel
{
    FormPurpose Purpose { get; }

    string Title { get; }

    string Context { get; }

    IReadOnlyList<IFormFieldViewModel> Fields { get; }

    bool AwaitsYou { get; }

    string Verdict { get; }

    string Note { get; set; }

    string Error { get; }

    IAsyncRelayCommand SubmitCommand { get; }

    IAsyncRelayCommand DeclineCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class FormCardViewModel : IFormCardViewModel, ITimelineItem
{
    private readonly HumanReplies replies;
    private readonly FormFieldViewModel[] fields;
    private FormEntry form;

    public FormCardViewModel(FormEntry entry, HumanReplies replies)
    {
        this.replies = replies;
        form = entry;
        fields = [.. entry.Form.Fields.Select(field => new FormFieldViewModel(field))];
        Note = string.Empty;
        Verdict = string.Empty;
        Error = string.Empty;

        foreach (var field in fields)
        {
            field.PropertyChanged += (_, _) => SubmitCommand.NotifyCanExecuteChanged();
        }

        Update(entry);
    }

    public FormPurpose Purpose => form.Form.Purpose;

    public string Title => form.Form.Title;

    public string Context => form.Form.Context;

    public IReadOnlyList<IFormFieldViewModel> Fields => fields;

    public bool IsShown => true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand), nameof(DeclineCommand))]
    public partial bool AwaitsYou { get; private set; }

    [ObservableProperty]
    public partial string Verdict { get; private set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    [ObservableProperty]
    public partial string Error { get; private set; }

    public void Update(ITimelineEntry entry)
    {
        if (entry is FormEntry changed)
        {
            form = changed;
            AwaitsYou = changed.AwaitsHuman;
            Verdict = CardPhrases.Verdict(changed);
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand), nameof(DeclineCommand))]
    public partial bool IsAnswering { get; private set; }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private Task SubmitAsync(CancellationToken cancellationToken) =>
        AnswerAsync(FormReplies.Answer(form.Item, [.. fields.Select(field => field.Choice())]), cancellationToken);

    [RelayCommand(CanExecute = nameof(CanDecline))]
    private Task DeclineAsync(CancellationToken cancellationToken) => AnswerAsync(FormReplies.Decline(form.Item, Note), cancellationToken);

    private async Task AnswerAsync(FormAnswer answer, CancellationToken cancellationToken)
    {
        IsAnswering = true;

        try
        {
            var answered = await replies.AnswerAsync(form, answer, cancellationToken);
            Error = answered.Match(_ => string.Empty, CardPhrases.Error);
        }
        finally
        {
            IsAnswering = false;
        }
    }

    private bool CanSubmit() => CanDecline() && FormReplies.IsComplete([.. fields.Select(field => field.Choice())]);

    private bool CanDecline() => AwaitsYou && !IsAnswering;
}
