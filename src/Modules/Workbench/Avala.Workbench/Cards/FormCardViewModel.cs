using Avala.Agents.Contracts.Events;
using Avala.Workbench.Conversation;
using Avala.Workbench.Replies;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Cards;

[INotifyPropertyChanged]
internal sealed partial class FormCardViewModel : ITimelineItem
{
    private readonly HumanReplies replies;
    private FormEntry form;

    public FormCardViewModel(FormEntry entry, HumanReplies replies)
    {
        this.replies = replies;
        form = entry;
        Fields = [.. entry.Form.Fields.Select(field => new FormFieldViewModel(field))];
        Note = string.Empty;
        Verdict = string.Empty;
        Error = string.Empty;

        foreach (var field in Fields)
        {
            field.PropertyChanged += (_, _) => SubmitCommand.NotifyCanExecuteChanged();
        }

        Update(entry);
    }

    public FormPurpose Purpose => form.Form.Purpose;

    public string Title => form.Form.Title;

    public string Context => form.Form.Context;

    public IReadOnlyList<FormFieldViewModel> Fields { get; }

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

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitAsync(CancellationToken cancellationToken)
    {
        var answered = await replies.AnswerAsync(form, FormReplies.Answer(form.Item, [.. Fields.Select(field => field.Choice())]), cancellationToken);
        Error = answered.Match(_ => string.Empty, CardPhrases.Error);
    }

    [RelayCommand(CanExecute = nameof(AwaitsYou))]
    private async Task DeclineAsync(CancellationToken cancellationToken)
    {
        var answered = await replies.AnswerAsync(form, FormReplies.Decline(form.Item, Note), cancellationToken);
        Error = answered.Match(_ => string.Empty, CardPhrases.Error);
    }

    private bool CanSubmit() => AwaitsYou && FormReplies.IsComplete([.. Fields.Select(field => field.Choice())]);
}
