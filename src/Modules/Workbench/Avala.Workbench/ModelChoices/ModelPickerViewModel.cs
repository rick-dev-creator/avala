using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.ModelChoices;

internal interface IModelPickerViewModel
{
    bool IsShown { get; }

    bool CanChoose { get; }

    IReadOnlyList<string> Models { get; }

    string Model { get; set; }

    bool IsEffortShown { get; }

    IReadOnlyList<string> Efforts { get; }

    string Effort { get; set; }

    string Note { get; }
}

internal sealed record ModelDefaults(string Model, string Effort);

[INotifyPropertyChanged]
internal sealed partial class ModelPickerViewModel : IModelPickerViewModel
{
    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    [ObservableProperty]
    public partial bool CanChoose { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<string> Models { get; private set; } = [];

    [ObservableProperty]
    public partial string Model { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsEffortShown { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<string> Efforts { get; private set; } = [];

    [ObservableProperty]
    public partial string Effort { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Note { get; private set; } = string.Empty;

    public ModelChoice Chosen =>
        CanChoose ? new ModelChoice(Picked(Models, Model), IsEffortShown ? Picked(Efforts, Effort) : Option<string>.None) : ModelChoice.Default;

    public void Offer(OffersModels offered, ModelDefaults defaults, ModelChoice selected, string note)
    {
        var keep = Chosen;
        Models = [defaults.Model, .. offered.Models];
        Efforts = [defaults.Effort, .. offered.Efforts];
        IsEffortShown = offered.Efforts.Count > 0;
        CanChoose = true;
        IsShown = true;
        Note = note;
        Model = Selected(Models, selected.Model.IsSome ? selected.Model : keep.Model);
        Effort = Selected(Efforts, selected.Effort.IsSome ? selected.Effort : keep.Effort);
    }

    public void Follow(ModelDefaults defaults, bool effort, string note)
    {
        Models = [defaults.Model];
        Efforts = [defaults.Effort];
        Model = defaults.Model;
        Effort = defaults.Effort;
        IsEffortShown = effort;
        CanChoose = false;
        IsShown = true;
        Note = note;
    }

    public void Hide(string note)
    {
        Models = [];
        Efforts = [];
        CanChoose = false;
        IsEffortShown = false;
        IsShown = false;
        Note = note;
    }

    private static string Selected(IReadOnlyList<string> offered, Option<string> wanted) =>
        wanted.Match(name => offered.Skip(1).Contains(name, StringComparer.Ordinal) ? name : offered[0], () => offered[0]);

    private static Option<string> Picked(IReadOnlyList<string> offered, string picked) =>
        offered.Count > 0 && picked != offered[0] && offered.Contains(picked, StringComparer.Ordinal) ? picked : Option<string>.None;
}
