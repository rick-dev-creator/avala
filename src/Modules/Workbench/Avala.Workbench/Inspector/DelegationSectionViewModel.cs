using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Workbench.Inspection;
using Avala.Workbench.Sidebar;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Inspector;

internal interface IDelegationSectionViewModel
{
    bool IsLoaded { get; }

    string Parent { get; }

    string Fact { get; }

    IReadOnlyList<string> Children { get; }

    bool IsEmpty { get; }
}

[INotifyPropertyChanged]
internal sealed partial class DelegationSectionViewModel : IDelegationSectionViewModel, IRegionAware<JobId>, IActivatable, IPresentation, IDisposable
{
    private readonly InspectedJob inspected;

    public DelegationSectionViewModel(InspectedJob inspected)
    {
        this.inspected = inspected;
        inspected.Showing(Show);
    }

    public event EventHandler<Presented>? Presented
    {
        add => inspected.Presented += value;
        remove => inspected.Presented -= value;
    }

    public long Revision => inspected.Revision;

    public Task Loading => inspected.Loading;

    [ObservableProperty]
    public partial bool IsLoaded { get; private set; }

    [ObservableProperty]
    public partial string Fact { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Parent { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<string> Children { get; private set; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    public void OnRegionContextChanged(Option<JobId> context) => inspected.Focus(context);

    public void Activate() => inspected.Activate();

    public void Deactivate() => inspected.Deactivate();

    public void Dispose() => inspected.Dispose();

    private void Show(Option<InspectorFacts> facts)
    {
        IsLoaded = facts.IsSome;
        var record = facts.Map(found => found.Record);
        Parent = record.Bind(found => found.Parent).Match(parent => $"Delegated by {FactPhrases.Title(parent.Instruction)}", () => string.Empty);
        Children = record.Match<IReadOnlyList<string>>(
            found =>
            [
                .. found.Children.Select(child => InspectorPhrases.Child(child, found.Delegated.FirstOrDefault(delegation => delegation.Child == child.Job).ToOption())),
                .. found.Delegated.Where(delegation => delegation.Child.IsNone).Select(InspectorPhrases.Refused),
            ],
            () => []);
        IsEmpty = facts.IsSome && Parent.Length == 0 && Children.Count == 0;
        Fact = record.Match(found => InspectorPhrases.Delegated(found.Children.Count, found.Parent.IsSome), () => string.Empty);
    }
}
