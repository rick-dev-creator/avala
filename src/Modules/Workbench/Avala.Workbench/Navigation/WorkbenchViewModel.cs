using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Workbench.Board;
using Avala.Workbench.Contracts.Presentation;
using Avala.Workbench.Conversation;
using Avala.Workbench.Presenting;
using Avala.Workbench.Review;
using Avala.Workbench.Steering;
using Avala.Workbench.Welcome;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Navigation;

internal interface IWorkbenchViewModel
{
    string Title { get; }

    IConversationViewModel? Conversation { get; }

    IReviewViewModel? Review { get; }

    ICandidateSwitcherViewModel? Candidates { get; }

    bool IsInspectorOpen { get; }

    IRelayCommand ToggleInspectorCommand { get; }

    IAsyncRelayCommand OpenReviewCommand { get; }

    IRelayCommand CloseReviewCommand { get; }

    IWelcomeViewModel Welcome { get; }

    JobRegions Regions { get; }
}

[INotifyPropertyChanged]
internal sealed partial class WorkbenchViewModel : IWorkbenchViewModel, IPage, IActivatable, IPresentation, IDisposable, IRecipient<JobSelected>
{
    private readonly BoardFeed feed;
    private readonly JobScreens screens;
    private readonly JobFocus focus;
    private Option<JobId> inspected;

    public WorkbenchViewModel(BoardFeed feed, JobScreens screens, JobFocus focus, IWelcomeViewModel welcome)
    {
        this.feed = feed;
        this.screens = screens;
        this.focus = focus;
        Welcome = welcome;
        focus.Follow(this);
    }

    public IWelcomeViewModel Welcome { get; }

    public JobRegions Regions => screens.Regions;

    public Task Checking { get; private set; } = Task.CompletedTask;

    public string Title => "Jobs";

    public PagePlacement Placement => PagePlacement.Hidden;

    public Task Following => feed.Following;

    public long Revision => feed.Revision;

    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    public Task Loading { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenReviewCommand))]
    public partial ConversationViewModel? Conversation { get; private set; }

    [ObservableProperty]
    public partial bool IsInspectorOpen { get; private set; }

    [ObservableProperty]
    public partial ReviewViewModel? Review { get; private set; }

    IConversationViewModel? IWorkbenchViewModel.Conversation => Conversation;

    IReviewViewModel? IWorkbenchViewModel.Review => Review;

    [ObservableProperty]
    public partial CandidateSwitcherViewModel? Candidates { get; private set; }

    ICandidateSwitcherViewModel? IWorkbenchViewModel.Candidates => Candidates;

    public Task Switching { get; private set; } = Task.CompletedTask;

    public void Activate()
    {
        feed.Start(Show);
        Checking = Welcome.CheckAsync(CancellationToken.None);
        Regions.Activate();
        Regions.Show(Conversation is { } open ? open.Job : Option<JobId>.None);
        Inspect();
    }

    public void Deactivate()
    {
        feed.Stop();
        Regions.Deactivate();
        Inspect();
    }

    public void Dispose() => Deactivate();

    public void Receive(JobSelected message)
    {
        if (Conversation?.Job != message.Job)
        {
            var opened = screens.Conversation(message.Job);
            _ = feed.Find(message.Job).Match(
                job =>
                {
                    opened.Show(job);
                    return true;
                },
                () => false);
            Review = null;
            Conversation = opened;
            Regions.Show(message.Job);
        }

        Inspect();
        focus.Show(this);
    }

    [RelayCommand]
    private void ToggleInspector()
    {
        IsInspectorOpen = !IsInspectorOpen;
        Inspect();
    }

    [RelayCommand(CanExecute = nameof(CanOpenReview))]
    private Task OpenReviewAsync()
    {
        var review = screens.Review(Conversation!.Job);
        review.Track(Conversation.Status);
        Review = review;
        Candidates = JobScreens.Candidates(review.Job);
        Candidates.Track(feed.Contests);
        Loading = feed.Find(review.Job).Match(
            job =>
            {
                review.Track(job);

                return feed.RunAsync(review.Request(job.Revision));
            },
            () => Task.CompletedTask);

        return Loading;
    }

    [RelayCommand]
    private void CloseReview() => Review = null;

    partial void OnReviewChanged(ReviewViewModel? oldValue, ReviewViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.Closed -= OnReviewClosed;
            oldValue.Resubmitted -= OnResubmitted;
        }

        if (newValue is not null)
        {
            newValue.Closed += OnReviewClosed;
            newValue.Resubmitted += OnResubmitted;
        }
        else
        {
            Candidates = null;
        }
    }

    partial void OnCandidatesChanged(CandidateSwitcherViewModel? oldValue, CandidateSwitcherViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.Switched -= OnCandidateSwitched;
        }

        if (newValue is not null)
        {
            newValue.Switched += OnCandidateSwitched;
        }
    }

    private void OnCandidateSwitched(object? sender, JobId job)
    {
        focus.Select(job);
        Switching = OpenReviewCommand.CanExecute(null) ? OpenReviewCommand.ExecuteAsync(null) : Task.CompletedTask;
    }

    private void OnReviewClosed(object? sender, EventArgs e) => Review = null;

    private void OnResubmitted(object? sender, JobId job) => focus.Select(job);

    private bool CanOpenReview() => Conversation is { } open && open.Status.CanBeReviewed;

    private void Inspect()
    {
        var job = feed.IsActive && IsInspectorOpen && Conversation is { } open ? Option<JobId>.Some(open.Job) : Option<JobId>.None;

        if (job != inspected)
        {
            inspected = job;
            focus.Inspect(job);
        }
    }

    private IReadOnlyList<Func<CancellationToken, Task>> Show(ImmutableDictionary<JobId, BoardJob> jobs)
    {
        if (Conversation is { } open && jobs.TryGetValue(open.Job, out var job))
        {
            open.Show(job);
            OpenReviewCommand.NotifyCanExecuteChanged();
        }

        Candidates?.Track(feed.Contests);

        if (Review is { } review && jobs.TryGetValue(review.Job, out var reviewed))
        {
            review.Track(reviewed);

            if (reviewed.Revision != review.Requested)
            {
                Loading = feed.RunAsync(review.Request(reviewed.Revision));
            }
        }

        return [];
    }
}
