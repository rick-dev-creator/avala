using Avala.Documentation.Contracts;
using Avala.Workbench.Documenting;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Docs;

internal interface IC4NodeViewModel
{
    string Key { get; }

    string Frame { get; }

    string Label { get; }

    string Kind { get; }

    string Technology { get; }

    bool IsPerson { get; }

    bool IsExternal { get; }

    bool IsDatabase { get; }

    bool IsHidden { get; }

    bool IsNew { get; }

    bool IsChanged { get; }

    bool IsRemoved { get; }

    bool IsFocus { get; }

    bool IsReached { get; }
}

[INotifyPropertyChanged]
internal sealed partial class C4NodeViewModel(C4Element element) : IC4NodeViewModel
{
    public string Key { get; } = element.Alias;

    public string Frame { get; } = element.Boundary.Match(boundary => boundary, () => string.Empty);

    public string Label { get; } = element.Label;

    public string Kind { get; } = DocsPhrases.Kind(element);

    public string Technology { get; } = element.Technology;

    public bool IsPerson { get; } = element.Shape == C4Shape.Person;

    public bool IsExternal { get; } = element.External;

    public bool IsDatabase { get; } = element.Shape == C4Shape.Database;

    [ObservableProperty]
    public partial bool IsHidden { get; private set; } = true;

    [ObservableProperty]
    public partial bool IsNew { get; private set; }

    [ObservableProperty]
    public partial bool IsChanged { get; private set; }

    [ObservableProperty]
    public partial bool IsRemoved { get; private set; }

    [ObservableProperty]
    public partial bool IsFocus { get; private set; }

    [ObservableProperty]
    public partial bool IsReached { get; private set; }

    public void Show(SceneNode node)
    {
        IsHidden = node.State == NodeState.Hidden;
        IsNew = node.State == NodeState.New;
        IsChanged = node.State == NodeState.Changed;
        IsRemoved = node.State == NodeState.Removed;
        IsFocus = node.Focus;
        IsReached = node.Reached;
    }
}
