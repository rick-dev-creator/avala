using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Workbench.Board;
using Avala.Workbench.Following;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Inspector;

internal interface IChecksRunningViewModel
{
    bool IsShown { get; }

    string Text { get; }

    IReadOnlyList<string> Lines { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ChecksRunningViewModel(JobBoard board, LiveFeed feed)
    : IChecksRunningViewModel, IRegionAware<JobId>, IActivatable, IPresentation, IDisposable
{
    private Option<JobId> job;

    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    public long Revision => feed.Revision;

    public Task Following => feed.Following;

    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    [ObservableProperty]
    public partial string Text { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<string> Lines { get; private set; } = [];

    public void Activate() => feed.Start(_ => ValueTask.FromResult(job.Bind(board.Find).Bind(found => found.PullRequest).Bind(Running)), Show);

    public void Deactivate() => feed.Stop();

    public void Dispose() => feed.Dispose();

    public void OnRegionContextChanged(Option<JobId> context)
    {
        job = context;
        feed.Refresh();
    }

    private static Option<PullRequestState> Running(PullRequestWatchState watch) =>
        watch.Status != WatchStatus.Ended && watch.Last.Match(last => last.Checks.Any(check => check.Status == CheckStatus.Pending), () => false)
            ? watch.Last
            : Option<PullRequestState>.None;

    private void Show(Option<PullRequestState> running)
    {
        IsShown = running.IsSome;
        Text = running.Match(ForgePhrases.Running, () => string.Empty);
        var lines = running.Match<List<string>>(found => [.. found.Checks.Select(ForgePhrases.Check)], () => []);
        Lines = Lines.SequenceEqual(lines) ? Lines : lines;
    }
}
