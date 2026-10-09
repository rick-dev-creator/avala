using System.ComponentModel;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Conversation;
using Avala.Workbench.Inspector;
using Avala.Workbench.Review;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Steering;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Navigation;

[INotifyPropertyChanged]
internal sealed partial class WorkbenchViewModel : IPage, IActivatable, IDisposable
{
    private readonly JobBoard board;
    private readonly IUiDispatcher ui;
    private readonly JobScreens screens;
    private CancellationTokenSource? active;

    public WorkbenchViewModel(JobBoard board, IUiDispatcher ui, SidebarViewModel sidebar, JobScreens screens)
    {
        this.board = board;
        this.ui = ui;
        this.screens = screens;
        Sidebar = sidebar;
        Sidebar.PropertyChanged += OnSidebarChanged;
    }

    public string Title => "Jobs";

    public SidebarViewModel Sidebar { get; }

    public Task Following { get; private set; } = Task.CompletedTask;

    public Task Loading { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenReviewCommand))]
    public partial ConversationViewModel? Conversation { get; private set; }

    [ObservableProperty]
    public partial bool IsInspectorOpen { get; private set; }

    [ObservableProperty]
    public partial InspectorViewModel? Inspector { get; private set; }

    [ObservableProperty]
    public partial ReviewViewModel? Review { get; private set; }

    public void Activate()
    {
        if (active is null)
        {
            active = new CancellationTokenSource();
            Following = FollowAsync(active.Token);
        }
    }

    public void Deactivate()
    {
        active?.Cancel();
        active?.Dispose();
        active = null;
    }

    public void Dispose() => Deactivate();

    [RelayCommand]
    private Task ToggleInspectorAsync()
    {
        IsInspectorOpen = !IsInspectorOpen;
        Inspector = IsInspectorOpen && Conversation is { } open ? screens.Inspector(open.Job) : null;

        return LoadAsync(Inspector is { } inspector ? inspector.Request : null);
    }

    [RelayCommand(CanExecute = nameof(CanOpenReview))]
    private Task OpenReviewAsync()
    {
        Review = screens.Review(Conversation!.Job);
        Review.Track(Conversation.Status);

        return LoadAsync(Review.Request);
    }

    [RelayCommand]
    private void CloseReview() => Review = null;

    private bool CanOpenReview() => Conversation is { } open && open.Status.CanBeReviewed;

    private Task LoadAsync(Func<int, Func<CancellationToken, Task>>? request)
    {
        if (request is null || active is not { } following || Conversation is not { } open || !board.Jobs.TryGetValue(open.Job, out var job))
        {
            return Task.CompletedTask;
        }

        Loading = GuardedAsync(request(job.Revision), following.Token);

        return Loading;
    }

    private static async Task GuardedAsync(Func<CancellationToken, Task> load, CancellationToken cancellationToken)
    {
        try
        {
            await load(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task FollowAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var _ in board.ChangesAsync(cancellationToken))
            {
                var refreshes = new List<Func<CancellationToken, Task>>();
                await ui.InvokeAsync(() => ShowWhileFollowing(refreshes, cancellationToken), cancellationToken);

                foreach (var refresh in refreshes)
                {
                    await refresh(cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void ShowWhileFollowing(List<Func<CancellationToken, Task>> refreshes, CancellationToken following)
    {
        if (!following.IsCancellationRequested)
        {
            Show(refreshes);
        }
    }

    private void Show(List<Func<CancellationToken, Task>> refreshes)
    {
        var jobs = board.Jobs;
        Sidebar.Show(jobs);

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
                refreshes.Add(review.Request(reviewed.Revision));
            }
        }

        if (Inspector is { } inspector && jobs.TryGetValue(inspector.Job, out var inspected) && inspected.Revision != inspector.Requested)
        {
            refreshes.Add(inspector.Request(inspected.Revision));
        }
    }

    private void OnSidebarChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName != nameof(SidebarViewModel.Selected))
        {
            return;
        }

        Conversation = Sidebar.Selected is { } row ? screens.Conversation(row.Job) : null;
        Review = null;
        Inspector = IsInspectorOpen && Conversation is { } selected ? screens.Inspector(selected.Job) : null;

        if (Conversation is { } opened)
        {
            board.Find(opened.Job).Match(
                job =>
                {
                    opened.Show(job);
                    return true;
                },
                () => false);
        }

        OpenReviewCommand.NotifyCanExecuteChanged();
        _ = LoadAsync(Inspector is { } inspector ? inspector.Request : null);
    }
}
