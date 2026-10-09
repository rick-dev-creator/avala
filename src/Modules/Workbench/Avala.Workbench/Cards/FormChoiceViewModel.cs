using Avala.Agents.Contracts.Events;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Cards;

internal interface IFormChoiceViewModel
{
    string Label { get; }

    string Description { get; }

    bool Recommended { get; }

    bool IsSelected { get; set; }
}

[INotifyPropertyChanged]
internal sealed partial class FormChoiceViewModel : IFormChoiceViewModel
{
    public FormChoiceViewModel(FormOption option)
    {
        Label = option.Label;
        Description = option.Description;
        Recommended = option.Recommended;
        IsSelected = option.Recommended;
    }

    public string Label { get; }

    public string Description { get; }

    public bool Recommended { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
