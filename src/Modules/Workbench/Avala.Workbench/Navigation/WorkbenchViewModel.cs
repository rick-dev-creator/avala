using System.ComponentModel;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Conversation;
using Avala.Workbench.Sidebar;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Navigation;

[INotifyPropertyChanged]
internal sealed partial class WorkbenchViewModel : IPage, IActivatable, IDisposable
{
    private readonly JobBoard board;
    private readonly IUiDispatcher ui;
    private readonly Conversations conversations;
    private CancellationTokenSource? active;

    public WorkbenchViewModel(JobBoard board, IUiDispatcher ui, SidebarViewModel sidebar, Conversations conversations)
    {
        this.board = board;
        this.ui = ui;
        this.conversations = conversations;
        Sidebar = sidebar;
        Sidebar.PropertyChanged += OnSidebarChanged;
    }

    public string Title => "Jobs";

    public SidebarViewModel Sidebar { get; }

    public Task Following { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    public partial ConversationViewModel? Conversation { get; private set; }

    [ObservableProperty]
    public partial bool IsInspectorOpen { get; private set; }

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
    private void ToggleInspector() => IsInspectorOpen = !IsInspectorOpen;

    private async Task FollowAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var _ in board.ChangesAsync(cancellationToken))
            {
                await ui.InvokeAsync(() => ShowWhileFollowing(cancellationToken), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void ShowWhileFollowing(CancellationToken following)
    {
        if (!following.IsCancellationRequested)
        {
            Show();
        }
    }

    private void Show()
    {
        var jobs = board.Jobs;
        Sidebar.Show(jobs);

        if (Conversation is { } open && jobs.TryGetValue(open.Job, out var job))
        {
            open.Show(job);
        }
    }

    private void OnSidebarChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName != nameof(SidebarViewModel.Selected))
        {
            return;
        }

        Conversation = Sidebar.Selected is { } row ? conversations.Open(row.Job) : null;

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
    }
}
