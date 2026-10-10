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

    IReadOnlyList<HandoffLine> Handoffs { get; }

    string Waiting { get; }
}

internal sealed record CapacityLine(string Connection, string Reading, bool IsChosen, bool IsAtLimit);

internal sealed record HandoffLine(string Moved, string Spent);

internal sealed record AutonomyLines(
    string Fact,
    string Autonomy,
    string Connection,
    string Model,
    string Reason,
    IReadOnlyList<CapacityLine> Compared,
    IReadOnlyList<HandoffLine> Handoffs,
    string Waiting)
{
    public static AutonomyLines None { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, [], [], string.Empty);

    public static AutonomyLines Of(InspectorFacts found)
    {
        var summary = found.Record.History.Summary;

        return new(
            found.Audit.Autonomy.Match(
                autonomy => autonomy.Effective.ToString(),
                () => summary.Autonomy.Match(requested => requested.ToString(), () => "not started")),
            found.Audit.Autonomy.Match(
                InspectorPhrases.Autonomy,
                () => summary.Autonomy.Match(requested => $"{requested}, as asked", () => "Not started yet")),
            summary.Connection.Match(connection => connection.Value, () => "The default connection"),
            found.Ran.Match(ModelPhrases.Ran, () => string.Empty),
            found.Choice.Match(InspectorPhrases.Chosen, () => string.Empty),
            found.Choice.Match<IReadOnlyList<CapacityLine>>(
                chosen => [.. chosen.Compared.Select(candidate => new CapacityLine(
                    candidate.Connection.Value,
                    InspectorPhrases.Capacity(candidate),
                    candidate.Connection == chosen.Connection,
                    !candidate.Available))],
                () => []),
            InspectorPhrases.Handoffs(found.Handoffs, found.Audit.Usage),
            found.Wait.Match(InspectorPhrases.Waiting, () => string.Empty));
    }
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

    [ObservableProperty]
    public partial IReadOnlyList<HandoffLine> Handoffs { get; private set; } = [];

    [ObservableProperty]
    public partial string Waiting { get; private set; } = string.Empty;

    public void OnRegionContextChanged(Option<JobId> context) => inspected.Focus(context);

    public void Activate() => inspected.Activate();

    public void Deactivate() => inspected.Deactivate();

    public void Dispose() => inspected.Dispose();

    private void Show(Option<InspectorFacts> facts)
    {
        var lines = facts.Match(AutonomyLines.Of, () => AutonomyLines.None);
        IsLoaded = facts.IsSome;
        Fact = lines.Fact;
        Autonomy = lines.Autonomy;
        Connection = lines.Connection;
        Model = lines.Model;
        Reason = lines.Reason;
        Compared = lines.Compared;
        Handoffs = lines.Handoffs;
        Waiting = lines.Waiting;
    }
}
