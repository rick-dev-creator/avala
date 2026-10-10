using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Workbench.Inspection;
using Avala.Workbench.Linking;
using Avala.Workbench.Presenting;
using Avala.Workbench.Reviewing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Inspector;

internal interface IPullRequestSectionViewModel
{
    bool IsLoaded { get; }

    bool HasPullRequest { get; }

    string Fact { get; }

    string Title { get; }

    string Link { get; }

    string Status { get; }

    string Policy { get; }

    string Checks { get; }

    IReadOnlyList<string> CheckLines { get; }

    string Mergeability { get; }

    IReadOnlyList<string> Reviews { get; }

    IReadOnlyList<string> WakeUps { get; }

    string Notice { get; }

    IAsyncRelayCommand OpenLinkCommand { get; }

    IAsyncRelayCommand CheckNowCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class PullRequestSectionViewModel : IPullRequestSectionViewModel, IRegionAware<JobId>, IActivatable, IPresentation, IDisposable
{
    private readonly InspectedJob inspected;
    private readonly Links links;
    private readonly ForgeDesk forge;
    private Option<JobId> shown;

    public PullRequestSectionViewModel(InspectedJob inspected, Links links, ForgeDesk forge)
    {
        this.inspected = inspected;
        this.links = links;
        this.forge = forge;
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
    [NotifyCanExecuteChangedFor(nameof(OpenLinkCommand), nameof(CheckNowCommand))]
    public partial bool HasPullRequest { get; private set; }

    [ObservableProperty]
    public partial string Fact { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Link { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Policy { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Checks { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<string> CheckLines { get; private set; } = [];

    [ObservableProperty]
    public partial string Mergeability { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<string> Reviews { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<string> WakeUps { get; private set; } = [];

    [ObservableProperty]
    public partial string Notice { get; private set; } = string.Empty;

    public void OnRegionContextChanged(Option<JobId> context) => inspected.Focus(context);

    public void Activate() => inspected.Activate();

    public void Deactivate() => inspected.Deactivate();

    public void Dispose() => inspected.Dispose();

    [RelayCommand(CanExecute = nameof(HasPullRequest))]
    private async Task OpenLinkAsync(CancellationToken cancellationToken) =>
        Notice = (await links.OpenAsync(Link, cancellationToken)).Match(_ => string.Empty, _ => $"The link could not be opened: {Link}");

    [RelayCommand(CanExecute = nameof(HasPullRequest))]
    private async Task CheckNowAsync(CancellationToken cancellationToken) =>
        Notice = await shown.Match(
            async job => (await forge.RefreshAsync(job, cancellationToken)).Match(_ => string.Empty, error => $"The check failed: {ForgePhrases.Error(error)}"),
            () => Task.FromResult(string.Empty));

    private void Show(Option<InspectorFacts> facts)
    {
        IsLoaded = facts.IsSome;
        shown = facts.Map(found => found.Record.History.Summary.Job);
        var watch = facts.Bind(found => found.PullRequest);
        var last = watch.Bind(state => state.Last);
        HasPullRequest = watch.IsSome;
        Fact = watch.Match(state => $"#{state.PullRequest.Number} · {ForgePhrases.Status(state).Split(" · ")[0]}", () => facts.IsSome ? "none" : string.Empty);
        Title = watch.Match(state => $"Pull request #{state.PullRequest.Number} on {state.Forge.Value}", () => facts.IsSome ? "No pull request was opened for this job." : string.Empty);
        Link = watch.Match(state => state.PullRequest.Link.ToString(), () => string.Empty);
        Status = watch.Match(ForgePhrases.Status, () => string.Empty);
        Policy = watch.Match(ForgePhrases.Policy, () => string.Empty);
        Checks = last.Match(ForgePhrases.Checks, () => watch.IsSome ? "Not checked yet" : string.Empty);
        CheckLines = last.Match<IReadOnlyList<string>>(state => [.. state.Checks.Select(ForgePhrases.Check)], () => []);
        Mergeability = last.Match(state => ForgePhrases.Mergeability(state.Mergeability), () => string.Empty);
        Reviews = last.Match<IReadOnlyList<string>>(state => [.. state.Reviews.Select(ForgePhrases.Review)], () => []);
        WakeUps = facts.Match<IReadOnlyList<string>>(found => [.. found.WakeUps.Select(ForgePhrases.WakeUp)], () => []);
    }
}
