using Avala.Agents.Contracts.Events;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Conversation;

internal sealed record ToolRun(ItemKind Kind, string Title, string Current, string SearchText);

internal interface IToolGroupViewModel
{
    FindMark Found { get; }

    ItemKind Kind { get; }

    string Title { get; }

    bool IsRunning { get; }

    string Current { get; }

    bool IsExpanded { get; }

    IRelayCommand ToggleCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ToolGroupViewModel : IToolGroupViewModel, ITimelineItem, ISearchable
{
    private string folded = string.Empty;

    public ToolGroupViewModel()
    {
        Title = string.Empty;
        Current = string.Empty;
    }

    public event EventHandler? Toggled;

    public bool IsShown => true;

    [ObservableProperty]
    public partial ItemKind Kind { get; private set; }

    [ObservableProperty]
    public partial string Title { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    public partial string Current { get; private set; }

    public bool IsRunning => Current.Length > 0;

    [ObservableProperty]
    public partial bool IsExpanded { get; private set; }

    [ObservableProperty]
    public partial FindMark Found { get; set; }

    public string SearchText => IsExpanded ? string.Empty : folded;

    public void Show(ToolRun run)
    {
        Kind = run.Kind;
        Title = run.Title;
        Current = run.Current;
        folded = run.SearchText;
    }

    public bool Unfold()
    {
        var wasFolded = !IsExpanded;

        if (wasFolded)
        {
            Toggle();
        }

        return wasFolded;
    }

    public void Update(ITimelineEntry entry)
    {
    }

    [RelayCommand]
    private void Toggle()
    {
        IsExpanded = !IsExpanded;
        Toggled?.Invoke(this, EventArgs.Empty);
    }
}
