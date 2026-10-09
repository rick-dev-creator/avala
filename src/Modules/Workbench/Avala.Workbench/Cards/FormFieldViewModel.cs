using System.ComponentModel;
using Avala.Agents.Contracts.Events;
using Avala.Workbench.Replies;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Cards;

[INotifyPropertyChanged]
internal sealed partial class FormFieldViewModel
{
    private readonly FormField source;

    public FormFieldViewModel(FormField field)
    {
        source = field;
        Choices = [.. field.Options.Select(option => new FormChoiceViewModel(option))];
        Text = string.Empty;

        foreach (var choice in Choices)
        {
            choice.PropertyChanged += OnChoiceChanged;
        }
    }

    public string Header => source.Header;

    public string Prompt => source.Prompt;

    public FieldKind Kind => source.Kind;

    public bool AcceptsFreeText => source.AcceptsFreeText;

    public IReadOnlyList<FormChoiceViewModel> Choices { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsComplete))]
    public partial string Text { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsComplete))]
    public partial bool Confirmed { get; set; }

    public bool IsComplete => Choice().IsComplete;

    public FieldChoice Choice() =>
        new(source, [.. Choices.Where(choice => choice.IsSelected).Select(choice => choice.Label)], Text, Confirmed);

    private void OnChoiceChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (sender is FormChoiceViewModel { IsSelected: true } selected && source.Kind == FieldKind.SingleChoice)
        {
            foreach (var other in Choices.Where(choice => choice != selected))
            {
                other.IsSelected = false;
            }
        }

        OnPropertyChanged(nameof(IsComplete));
    }
}
