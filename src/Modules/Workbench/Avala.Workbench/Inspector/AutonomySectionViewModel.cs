using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Workbench.Inspection;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Inspector;

internal interface IAutonomySectionViewModel
{
    bool IsLoaded { get; }

    string Autonomy { get; }

    string Connection { get; }
}

[INotifyPropertyChanged]
internal sealed partial class AutonomySectionViewModel : IAutonomySectionViewModel, IRegionAware<JobId>, IActivatable, IPresentation, IDisposable
{
    private readonly InspectedJob inspected;

    public AutonomySectionViewModel(InspectedJob inspected)
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
    public partial string Autonomy { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Connection { get; private set; } = string.Empty;

    public void OnRegionContextChanged(Option<JobId> context) => inspected.Focus(context);

    public void Activate() => inspected.Activate();

    public void Deactivate() => inspected.Deactivate();

    public void Dispose() => inspected.Dispose();

    private void Show(Option<InspectorFacts> facts)
    {
        IsLoaded = facts.IsSome;
        Autonomy = facts.Match(
            found => found.Audit.Autonomy.Match(
                InspectorPhrases.Autonomy,
                () => found.Record.History.Summary.Autonomy.Match(requested => $"{requested}, as asked", () => "Not started yet")),
            () => string.Empty);
        Connection = facts.Match(
            found => found.Record.History.Summary.Connection.Match(connection => connection.Value, () => "The default connection"),
            () => string.Empty);
    }
}
