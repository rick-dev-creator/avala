using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Workbench.Inspection;
using Avala.Workbench.Review;
using Avala.Workbench.Reviewing;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Inspector;

internal interface IEvidenceSectionViewModel
{
    bool IsLoaded { get; }

    string Summary { get; }

    string Fact { get; }

    IReadOnlyList<string> Attempts { get; }
}

[INotifyPropertyChanged]
internal sealed partial class EvidenceSectionViewModel : IEvidenceSectionViewModel, IRegionAware<JobId>, IActivatable, IPresentation, IDisposable
{
    private readonly InspectedJob inspected;

    public EvidenceSectionViewModel(InspectedJob inspected)
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
    public partial string Summary { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<string> Attempts { get; private set; } = [];

    public void OnRegionContextChanged(Option<JobId> context) => inspected.Focus(context);

    public void Activate() => inspected.Activate();

    public void Deactivate() => inspected.Deactivate();

    public void Dispose() => inspected.Dispose();

    private void Show(Option<InspectorFacts> facts)
    {
        IsLoaded = facts.IsSome;
        Fact = facts.Match(found => InspectorPhrases.Checked(found.Audit.Verifications), () => string.Empty);
        Summary = facts.Match(
            found => ReviewPhrases.Verdict(ReviewExceptions.VerdictOf(found.Audit.Verifications, found.Record.History.Attempts.Count)),
            () => string.Empty);
        Attempts = facts.Match<IReadOnlyList<string>>(found => InspectorPhrases.Attempts(found.Record.History.Attempts, found.Audit.Verifications), () => []);
    }
}
