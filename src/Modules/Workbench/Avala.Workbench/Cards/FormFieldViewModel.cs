using System.ComponentModel;
using Avala.Agents.Contracts.Events;
using Avala.Workbench.Replies;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Cards;

internal interface IFormFieldViewModel
{
    string Header { get; }

    string Prompt { get; }

    FieldKind Kind { get; }

    bool AcceptsText { get; }

    bool IsConfirmation { get; }

    IReadOnlyList<IFormChoiceViewModel> Choices { get; }

    string Text { get; set; }

    bool Confirmed { get; set; }

    bool IsComplete { get; }
}

[INotifyPropertyChanged]
internal sealed partial class FormFieldViewModel : IFormFieldViewModel
{
    private readonly FormField source;
    private readonly FormChoiceViewModel[] choices;

    public FormFieldViewModel(FormField field)
    {
        source = field;
        choices = [.. field.Options.Select(option => new FormChoiceViewModel(option))];
        Text = string.Empty;

        foreach (var choice in choices)
        {
            choice.PropertyChanged += OnChoiceChanged;
        }
    }

    public string Header => source.Header;

    public string Prompt => source.Prompt;

    public FieldKind Kind => source.Kind;

    public bool AcceptsText => source.Kind == FieldKind.FreeText || source.AcceptsFreeText;

    public bool IsConfirmation => source.Kind == FieldKind.Confirmation;

    public IReadOnlyList<IFormChoiceViewModel> Choices => choices;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsComplete))]
    public partial string Text { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsComplete))]
    public partial bool Confirmed { get; set; }

    public bool IsComplete => Choice().IsComplete;

    public FieldChoice Choice() =>
        new(source, [.. choices.Where(choice => choice.IsSelected).Select(choice => choice.Label)], Text, Confirmed);

    private void OnChoiceChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (sender is FormChoiceViewModel { IsSelected: true } selected && source.Kind == FieldKind.SingleChoice)
        {
            foreach (var other in choices.Where(choice => choice != selected))
            {
                other.IsSelected = false;
            }
        }

        OnPropertyChanged(nameof(IsComplete));
    }
}
