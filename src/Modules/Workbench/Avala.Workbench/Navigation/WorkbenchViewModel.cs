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
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Navigation;

internal interface IWorkbenchViewModel
{
    string Title { get; }

    IConversationViewModel? Conversation { get; }

    IReviewViewModel? Review { get; }

    bool IsInspectorOpen { get; }

    IRelayCommand ToggleInspectorCommand { get; }

    IAsyncRelayCommand OpenReviewCommand { get; }

    IRelayCommand CloseReviewCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class WorkbenchViewModel : IWorkbenchViewModel, IPage, IActivatable, IPresentation, IDisposable, IRecipient<JobSelected>
{
    private readonly BoardFeed feed;
    private readonly JobScreens screens;
    private readonly JobFocus focus;
    private Option<JobId> inspected;

    public WorkbenchViewModel(BoardFeed feed, JobScreens screens, JobFocus focus)
    {
        this.feed = feed;
        this.screens = screens;
        this.focus = focus;
        focus.Follow(this);
    }

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

    public void Activate()
    {
        feed.Start(Show);
        Inspect();
    }

    public void Deactivate()
    {
        feed.Stop();
        Inspect();
    }

    public void Dispose() => Deactivate();

    public void Receive(JobSelected message)
    {
        if (Conversation?.Job != message.Job)
        {
            var opened = screens.Conversation(message.Job);
            Review = null;
            Conversation = opened;
            _ = feed.Find(message.Job).Match(
                job =>
                {
                    opened.Show(job);
                    return true;
                },
                () => false);
            OpenReviewCommand.NotifyCanExecuteChanged();
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
        Loading = feed.Find(review.Job).Match(job => feed.RunAsync(review.Request(job.Revision)), () => Task.CompletedTask);

        return Loading;
    }

    [RelayCommand]
    private void CloseReview() => Review = null;

    partial void OnReviewChanged(ReviewViewModel? oldValue, ReviewViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.Closed -= OnReviewClosed;
        }

        if (newValue is not null)
        {
            newValue.Closed += OnReviewClosed;
        }
    }

    private void OnReviewClosed(object? sender, EventArgs e) => Review = null;

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

        if (Review is { } review && jobs.TryGetValue(review.Job, out var reviewed))
        {
            review.Track(reviewed.Status);

            if (reviewed.Revision != review.Requested)
            {
                return [review.Request(reviewed.Revision)];
            }
        }

        return [];
    }
}
