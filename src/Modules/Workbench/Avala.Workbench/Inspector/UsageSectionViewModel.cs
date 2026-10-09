using Avala.Agents.Contracts.Events;
using Avala.Components.Meters;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Workbench.Inspection;
using Avala.Workbench.Review;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Inspector;

internal interface IUsageSectionViewModel
{
    bool IsLoaded { get; }

    string Spent { get; }

    string Fact { get; }

    bool IsAttention { get; }

    IMeterViewModel? Meter { get; }

    IReadOnlyList<string> Caps { get; }

    IReadOnlyList<string> Interventions { get; }

    string Carve { get; }
}

[INotifyPropertyChanged]
internal sealed partial class UsageSectionViewModel : IUsageSectionViewModel, IRegionAware<JobId>, IActivatable, IPresentation, IDisposable
{
    private readonly InspectedJob inspected;

    public UsageSectionViewModel(InspectedJob inspected)
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
    public partial bool IsAttention { get; private set; }

    [ObservableProperty]
    public partial IMeterViewModel? Meter { get; private set; }

    [ObservableProperty]
    public partial string Spent { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<string> Caps { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<string> Interventions { get; private set; } = [];

    [ObservableProperty]
    public partial string Carve { get; private set; } = string.Empty;

    public void OnRegionContextChanged(Option<JobId> context) => inspected.Focus(context);

    public void Activate() => inspected.Activate();

    public void Deactivate() => inspected.Deactivate();

    public void Dispose() => inspected.Dispose();

    private void Show(Option<InspectorFacts> facts)
    {
        IsLoaded = facts.IsSome;
        var audit = facts.Match(found => found.Audit, () => new AuditFacts([], [], []));
        var cap = audit.Budget.Bind(budget => budget.Caps.CostPerJob.Count > 0 ? Option<Cost>.Some(budget.Caps.CostPerJob[0]) : Option<Cost>.None);
        Fact = facts.IsNone ? string.Empty : InspectorPhrases.Spending(audit.Usage, cap);
        IsAttention = audit.Interventions.Count > 0;
        Meter = InspectorPhrases.Share(audit.Usage, cap).Match<IMeterViewModel?>(
            share =>
            {
                var meter = new MeterViewModel("Cost cap", Option<double>.None);
                meter.Show(share);

                return meter;
            },
            () => null);
        Spent = facts.IsNone ? string.Empty : audit.Usage.Match(usage => string.Join(" · ", Amounts.Spent(usage)), () => "No usage reported");
        Caps = audit.Budget.Match(budget => InspectorPhrases.Caps(budget.Caps), () => []);
        Interventions = [.. audit.Interventions.Select(InspectorPhrases.Intervention)];
        Carve = audit.Carve.Match(InspectorPhrases.Carve, () => string.Empty);
    }
}
