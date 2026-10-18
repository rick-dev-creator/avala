using System.Collections.ObjectModel;
using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Workbench.Contracts.Presentation;
using Avala.Workbench.Documenting;
using Avala.Workbench.Following;
using Avala.Workbench.Sidebar;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Docs;

internal interface IDocsPaneViewModel
{
    bool IsOpen { get; }

    string Feature { get; }

    string State { get; }

    string Problem { get; }

    bool IsWriting { get; }

    IC4PlayerViewModel Player { get; }

    IJobHistoryViewModel History { get; }

    IReadOnlyList<IFeatureRowViewModel> Others { get; }

    bool CanOpenSite { get; }

    string SiteHint { get; }

    IAsyncRelayCommand OpenSiteCommand { get; }

    IRelayCommand CloseCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class DocsPaneViewModel
    : IDocsPaneViewModel, IPane, IRegionAware<JobId>, IActivatable, IPresentation, IRecipient<DocsPaneRequested>, IDisposable
{
    private readonly DocsReader reader;
    private readonly LiveFeed feed;
    private readonly IMessenger messenger;
    private readonly C4PlayerViewModel player;
    private readonly JobHistoryViewModel history = new();
    private readonly ObservableCollection<FeatureRowViewModel> others = [];
    private Option<JobId> job;
    private Option<JobId> shownJob;
    private string played = string.Empty;
    private Option<FeatureStory> stories;
    private DocsSite site = new(false, "docs", false);

    public DocsPaneViewModel(DocsReader reader, LiveFeed feed, IMessenger messenger, C4PlayerViewModel player)
    {
        this.reader = reader;
        this.feed = feed;
        this.messenger = messenger;
        this.player = player;
        player.IsCompact = true;
        history.VersionChosen += (_, number) => player.Version = Index(number);
        history.WriterOpened += (_, writer) => messenger.Send(new JobSelected(writer));
        messenger.RegisterAll(this);
    }

    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    public long Revision => feed.Revision;

    public Task Following => feed.Following;

    public IC4PlayerViewModel Player => player;

    public IJobHistoryViewModel History => history;

    public IReadOnlyList<IFeatureRowViewModel> Others => others;

    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    public partial string Feature { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string State { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Problem { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsWriting { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenSiteCommand))]
    public partial bool CanOpenSite { get; private set; }

    [ObservableProperty]
    public partial string SiteHint { get; private set; } = string.Empty;

    public void Activate() => feed.Start(ReadAsync, Show, Fail);

    public void Deactivate() => feed.Stop();

    public void Dispose()
    {
        feed.Dispose();
        player.Dispose();
        messenger.UnregisterAll(this);
    }

    public void OnRegionContextChanged(Option<JobId> context)
    {
        job = context;
        Toggle(IsOpen && job.Match(reader.Offers, () => false));
        feed.Refresh();
    }

    public void Receive(DocsPaneRequested message)
    {
        if (job == Option<JobId>.Some(message.Job))
        {
            Toggle(!IsOpen);
            feed.Refresh();
        }
    }

    public void Close() => Toggle(false);

    [RelayCommand]
    private void CloseNow() => Toggle(false);

    IRelayCommand IDocsPaneViewModel.CloseCommand => CloseNowCommand;

    private async ValueTask<Option<JobDocs>> ReadAsync(CancellationToken cancellationToken) =>
        IsOpen && job.Match<JobId?>(found => found, () => null) is { } viewing ? await reader.JobAsync(viewing, cancellationToken) : Option<JobDocs>.None;

    private void Show(Option<JobDocs> read)
    {
        Problem = string.Empty;

        foreach (var docs in read.Match<JobDocs[]>(found => [found], () => []))
        {
            var documented = docs.Documentation;
            shownJob = documented.Job;
            Feature = documented.Feature.Title;
            IsWriting = documented.Writing.IsSome;
            State = docs.Unplanned ? DocsPanePhrases.NoPlan : DocsPanePhrases.State(documented);
            Problem = DocsPanePhrases.Unplayed(docs.Unplayed);
            var story = docs.Story.Match(found => string.Join(',', found.Versions.Select(version => version.File)), () => string.Empty);

            if (story != played)
            {
                played = story;
                player.Show(docs.Story);
            }

            history.Show(docs);
            site = docs.Site;
            CanOpenSite = docs.Site.Exists && docs.Site.CanServe;
            SiteHint = docs.Site.Exists && !docs.Site.CanServe ? DocsPanePhrases.Install : string.Empty;
            stories = docs.Story;

            others.Clear();

            foreach (var listed in docs.Docs.Features.Where(listed => listed.Feature.Slug != documented.Feature.Slug))
            {
                others.Add(new FeatureRowViewModel(listed, () => messenger.Send(FeatureDocsRequested.Of(docs.Repository, listed))));
            }
        }
    }

    private void Fail(Exception exception)
    {
        IsWriting = false;
        State = DocsPanePhrases.Unreadable;
        Problem = DocsPanePhrases.Problem(exception.Message);

        if (shownJob != job)
        {
            Forget();
        }
    }

    private void Forget()
    {
        shownJob = job;
        Feature = job.Match(reader.FeatureOf, () => string.Empty);
        played = string.Empty;
        player.Show(Option<FeatureStory>.None);
        history.Clear();
        stories = Option<FeatureStory>.None;
        others.Clear();
        CanOpenSite = false;
        SiteHint = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanOpenSite))]
    private async Task OpenSiteAsync(CancellationToken cancellationToken)
    {
        foreach (var current in job.Match<JobId[]>(found => [found], () => []))
        {
            SiteHint = (await reader.OpenSiteAsync(current, site, cancellationToken)).Match(_ => DocsPanePhrases.Install, () => string.Empty);
        }
    }

    private int Index(int number) =>
        stories.Match(story => story.Versions.ToList().FindIndex(version => version.Number == number), () => -1) is var found and >= 0 ? found : player.Version;

    private void Toggle(bool open)
    {
        if (IsOpen == open)
        {
            return;
        }

        IsOpen = open;

        foreach (var current in job.Match<JobId[]>(found => [found], () => []))
        {
            messenger.Send(new DocsPaneChanged(current, open));
        }
    }
}

internal static class DocsPanePhrases
{
    public const string Install = "Install DocFX to open the docs site: " + DocsSite.Install;

    public const string NoPlan = "No plan yet: the agent has not recorded a task list, so there is nothing to document.";

    public const string Unreadable = "unreadable";

    public static string Problem(string reason) => $"Could not read this feature's docs: {reason}";

    public static string Unplayed(IReadOnlyList<UnplayedVersion> unplayed) =>
        string.Join(' ', unplayed.Select(version => version.Repeated.Match(
            name => $"{version.File} names {name} twice in its diagram, so the story leaves it out.",
            () => version.After.Match(
                after => $"{version.File} could not be played after {after}, so the story leaves it out.",
                () => $"{version.File} could not be played, so the story leaves it out."))));

    public static string State(JobDocumentation documented) =>
        documented.Writing.IsSome ? "writing"
        : documented.Versions.Count == 0 ? "waiting for a plan"
        : documented.Versions.Count == 1 ? "1 version"
        : $"{documented.Versions.Count} versions";
}
