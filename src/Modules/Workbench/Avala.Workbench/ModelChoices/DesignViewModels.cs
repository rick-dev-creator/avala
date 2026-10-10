using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.ModelChoices;

[INotifyPropertyChanged]
internal sealed partial class DesignModelPickerViewModel : IModelPickerViewModel
{
    public bool IsShown { get; init; } = true;

    public bool CanChoose { get; init; } = true;

    public IReadOnlyList<string> Models { get; init; } = ["Default (sonnet)", "opus", "sonnet", "haiku"];

    [ObservableProperty]
    public partial string Model { get; set; } = "opus";

    public bool IsEffortShown { get; init; } = true;

    public IReadOnlyList<string> Efforts { get; init; } = ["Default (medium)", "low", "medium", "high", "xhigh", "max"];

    [ObservableProperty]
    public partial string Effort { get; set; } = "high";

    public string Note { get; init; } = "Default is the connection's own choice, from its settings in connections.json, else the harness's.";
}
