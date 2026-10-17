using Avala.Documentation.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Docs;

internal interface IDocSectionViewModel
{
    string Anchor { get; }

    string Title { get; }

    string Text { get; }

    string Mark { get; }

    bool IsNew { get; }

    bool IsChanged { get; }

    bool IsRemoved { get; }

    bool IsFlow { get; }

    bool IsActive { get; }

    IRelayCommand ChooseCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class DocSectionViewModel(PlayStep step, Action<string> choose) : IDocSectionViewModel
{
    public string Anchor { get; } = step.Anchor;

    public string Title { get; } = step.Title;

    public string Text { get; } = step.Text;

    public string Mark { get; } = DocsPhrases.Mark(step.Kind);

    public bool IsNew { get; } = step.Kind == StepKind.Added;

    public bool IsChanged { get; } = step.Kind == StepKind.Changed;

    public bool IsRemoved { get; } = step.Kind == StepKind.Removed;

    public bool IsFlow { get; } = step.Kind == StepKind.Flow;

    [ObservableProperty]
    public partial bool IsActive { get; private set; }

    public void Activate(bool active) => IsActive = active;

    [RelayCommand]
    private void Choose() => choose(Anchor);
}
