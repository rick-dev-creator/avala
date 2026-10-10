using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Workbench.Inspection;
using Avala.Workbench.ModelChoices;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Inspector;

internal interface IAutonomySectionViewModel
{
    bool IsLoaded { get; }

    string Autonomy { get; }

    string Fact { get; }

    string Connection { get; }

    string Model { get; }

    string Reason { get; }

    IReadOnlyList<CapacityLine> Compared { get; }
}

internal sealed record CapacityLine(string Connection, string Reading, bool IsChosen, bool IsAtLimit);

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
    public partial string Fact { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Autonomy { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Connection { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Model { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Reason { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<CapacityLine> Compared { get; private set; } = [];

    public void OnRegionContextChanged(Option<JobId> context) => inspected.Focus(context);

    public void Activate() => inspected.Activate();

    public void Deactivate() => inspected.Deactivate();

    public void Dispose() => inspected.Dispose();

    private void Show(Option<InspectorFacts> facts)
    {
        IsLoaded = facts.IsSome;
        Fact = facts.Match(
            found => found.Audit.Autonomy.Match(
                autonomy => autonomy.Effective.ToString(),
                () => found.Record.History.Summary.Autonomy.Match(requested => requested.ToString(), () => "not started")),
            () => string.Empty);
        Autonomy = facts.Match(
            found => found.Audit.Autonomy.Match(
                InspectorPhrases.Autonomy,
                () => found.Record.History.Summary.Autonomy.Match(requested => $"{requested}, as asked", () => "Not started yet")),
            () => string.Empty);
        Connection = facts.Match(
            found => found.Record.History.Summary.Connection.Match(connection => connection.Value, () => "The default connection"),
            () => string.Empty);
        Model = facts.Bind(found => found.Ran).Match(ModelPhrases.Ran, () => string.Empty);
        var choice = facts.Bind(found => found.Choice);
        Reason = choice.Match(InspectorPhrases.Chosen, () => string.Empty);
        Compared = choice.Match<IReadOnlyList<CapacityLine>>(
            chosen => [.. chosen.Compared.Select(candidate => new CapacityLine(
                candidate.Connection.Value,
                InspectorPhrases.Capacity(candidate),
                candidate.Connection == chosen.Connection,
                !candidate.Available))],
            () => []);
    }
}
