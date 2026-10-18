using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Verification.Contracts;
using Avala.Workbench.Inspection;
using Avala.Workbench.Readying;
using Avala.Workbench.Review;
using Avala.Workbench.Reviewing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Inspector;

internal interface IEvidenceSectionViewModel
{
    bool IsLoaded { get; }

    string Summary { get; }

    string Fact { get; }

    IReadOnlyList<string> Attempts { get; }

    string Lacks { get; }

    IAsyncRelayCommand DeclareChecksCommand { get; }

    string ReportedTitle { get; }

    IReadOnlyList<string> Reported { get; }
}

[INotifyPropertyChanged]
internal sealed partial class EvidenceSectionViewModel : IEvidenceSectionViewModel, IRegionAware<JobId>, IActivatable, IPresentation, IDisposable
{
    private readonly InspectedJob inspected;
    private readonly IReadinessLinks links;
    private string repository = string.Empty;

    public EvidenceSectionViewModel(InspectedJob inspected, IReadinessLinks links)
    {
        this.inspected = inspected;
        this.links = links;
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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeclareChecksCommand))]
    public partial string Lacks { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ReportedTitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<string> Reported { get; private set; } = [];

    public void OnRegionContextChanged(Option<JobId> context) => inspected.Focus(context);

    public void Activate() => inspected.Activate();

    public void Deactivate() => inspected.Deactivate();

    public void Dispose() => inspected.Dispose();

    [RelayCommand(CanExecute = nameof(CanDeclareChecks))]
    private Task DeclareChecksAsync() => links.DeclareChecksAsync(repository);

    private bool CanDeclareChecks() => Lacks.Length > 0;

    private void Show(Option<InspectorFacts> facts)
    {
        IsLoaded = facts.IsSome;
        Fact = facts.Match(found => InspectorPhrases.Checked(found.Audit.Verifications), () => string.Empty);
        Summary = facts.Match(
            found => ReviewPhrases.Verdict(ReviewExceptions.VerdictOf(found.Audit.Verifications, found.Record.History.Attempts.Count)),
            () => string.Empty);
        Attempts = facts.Match<IReadOnlyList<string>>(found => InspectorPhrases.Attempts(found.Record.History.Attempts, found.Audit.Verifications), () => []);
        var undeclared = facts.Match(found => found.Audit.Verifications is [.., { Outcome: VerificationOutcome.NoChecksDeclared }], () => false);
        var reported = facts.Match<IReadOnlyList<ReportedRun>>(found => undeclared ? found.Reported : [], () => []);
        repository = facts.Match(found => found.Record.History.Summary.Repository, () => string.Empty);
        Lacks = undeclared && repository.Length > 0 ? ReviewPhrases.NoChecksFile : string.Empty;
        ReportedTitle = reported.Count > 0 ? ReviewPhrases.ReportedTitle : string.Empty;
        Reported = [.. reported.Select(ReviewPhrases.Ran)];
    }
}
