using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Conversation;

internal sealed record SearchFound(IReadOnlyList<int> Matches, int Current);

internal interface IConversationSearchViewModel
{
    bool IsOpen { get; }

    string Query { get; set; }

    string Status { get; }

    bool HasMatches { get; }

    int Revealed { get; }

    IRelayCommand OpenCommand { get; }

    IRelayCommand CloseCommand { get; }

    IRelayCommand NextCommand { get; }

    IRelayCommand PreviousCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ConversationSearchViewModel(Func<IReadOnlyList<string>> texts) : IConversationSearchViewModel
{
    private List<int> matches = [];
    private int current = -1;

    public event EventHandler<SearchFound>? Found;

    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasMatches { get; private set; }

    [ObservableProperty]
    public partial int Revealed { get; private set; } = -1;

    public void Refresh()
    {
        if (IsOpen)
        {
            Find(keep: true);
        }
    }

    partial void OnQueryChanged(string value) => Find(keep: false);

    [RelayCommand]
    private void Open()
    {
        IsOpen = true;
        Find(keep: true);
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        Show([], -1, string.Empty);
    }

    [RelayCommand]
    private void Next() => Step(1);

    [RelayCommand]
    private void Previous() => Step(-1);

    private void Find(bool keep)
    {
        if (!IsOpen)
        {
            return;
        }

        var query = Query.Trim();
        var kept = keep && current >= 0 && current < matches.Count ? matches[current] : -1;
        var all = texts();
        var found = query.Length == 0
            ? []
            : Enumerable.Range(0, all.Count).Where(index => all[index].Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
        var at = found.Count == 0 ? -1 : Math.Max(0, found.IndexOf(kept));
        Show(found, at, query);
    }

    private void Step(int by)
    {
        if (matches.Count > 0)
        {
            Show(matches, (current + by + matches.Count) % matches.Count, Query.Trim());
        }
    }

    private void Show(List<int> found, int at, string query)
    {
        matches = found;
        current = at;
        HasMatches = matches.Count > 0;
        Status = query.Length == 0 ? string.Empty : HasMatches ? $"{current + 1} of {matches.Count}" : "No matches";
        Revealed = current >= 0 ? matches[current] : -1;
        Found?.Invoke(this, new SearchFound(matches, current));
    }
}
