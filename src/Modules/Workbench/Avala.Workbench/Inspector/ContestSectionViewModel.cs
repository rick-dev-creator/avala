using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Workbench.Inspection;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Inspector;

internal interface IContestSectionViewModel
{
    bool IsLoaded { get; }

    bool IsContested { get; }

    string Fact { get; }

    string Status { get; }

    IReadOnlyList<string> Ranking { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ContestSectionViewModel : IContestSectionViewModel, IRegionAware<JobId>, IActivatable, IPresentation, IDisposable
{
    private readonly InspectedJob inspected;

    public ContestSectionViewModel(InspectedJob inspected)
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
    public partial bool IsContested { get; private set; }

    [ObservableProperty]
    public partial string Fact { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<string> Ranking { get; private set; } = [];

    public void OnRegionContextChanged(Option<JobId> context) => inspected.Focus(context);

    public void Activate() => inspected.Activate();

    public void Deactivate() => inspected.Deactivate();

    public void Dispose() => inspected.Dispose();

    private void Show(Option<InspectorFacts> facts)
    {
        IsLoaded = facts.IsSome;
        var contest = facts.Bind(found => found.Contest.Map(record => (Record: record, Job: found.Record.History.Summary.Job)));
        IsContested = contest.IsSome;
        Fact = contest.Match(found => ContestPhrases.Fact(found.Record, found.Job), () => string.Empty);
        Status = contest.Match(found => ContestPhrases.Status(found.Record), () => string.Empty);
        Ranking = contest.Match(found => ContestPhrases.Ranking(found.Record, found.Job), () => []);
    }
}
