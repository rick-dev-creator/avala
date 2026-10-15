using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Avala.Jobs.Contracts;
using Avala.Workbench.Mentioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Conversation;

internal sealed record MentionChosen(string Draft);

internal interface IMentionsViewModel
{
    IReadOnlyList<IMentionRowViewModel> Suggestions { get; }

    bool IsOpen { get; }

    IRelayCommand NextCommand { get; }

    IRelayCommand PreviousCommand { get; }

    IRelayCommand ChooseCommand { get; }

    IRelayCommand DismissCommand { get; }
}

internal interface IMentionRowViewModel
{
    string Label { get; }

    string Detail { get; }

    MentionKind Kind { get; }

    bool IsSelected { get; }

    IRelayCommand ChooseCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class MentionsViewModel(JobId job, MentionSource source) : IMentionsViewModel
{
    private readonly ObservableCollection<MentionRowViewModel> suggestions = [];
    private readonly List<Mention> chosen = [];
    private string draft = string.Empty;
    private int typed = -1;
    private int selected = -1;

    public event EventHandler<MentionChosen>? Chosen;

    public IReadOnlyList<IMentionRowViewModel> Suggestions => suggestions;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(PreviousCommand), nameof(ChooseCommand), nameof(DismissCommand))]
    public partial bool IsOpen { get; private set; }

    public Task Looking { get; private set; } = Task.CompletedTask;

    public void Look(string text)
    {
        draft = text;
        var match = Typed().Match(text);
        typed = match.Success ? match.Groups["query"].Index : -1;
        if (match.Success)
        {
            Looking = SuggestAsync(match.Groups["query"].Value);
            return;
        }

        Close();
        Looking = Task.CompletedTask;
    }

    public string References(string text)
    {
        var mentioned = chosen.Where(mention => text.Contains($"@{mention.Token}", StringComparison.Ordinal)).DistinctBy(mention => mention.Token).ToList();

        return mentioned.Count == 0
            ? text
            : $"{text}\n\nReferenced:\n{string.Join('\n', mentioned.Select(mention => $"- @{mention.Token}: {mention.Reference}"))}";
    }

    public void Forget() => chosen.Clear();

    [RelayCommand(CanExecute = nameof(IsOpen))]
    private void Next() => Select(selected + 1);

    [RelayCommand(CanExecute = nameof(IsOpen))]
    private void Previous() => Select(selected - 1);

    [RelayCommand(CanExecute = nameof(IsOpen))]
    private void Choose()
    {
        if (selected < 0 || selected >= suggestions.Count || typed < 1)
        {
            return;
        }

        var mention = suggestions[selected].Mention;
        chosen.Add(mention);
        var written = $"{draft[..typed]}{mention.Token} ";
        Close();
        Chosen?.Invoke(this, new MentionChosen(written));
    }

    private void Pick(int row)
    {
        Select(row);
        Choose();
    }

    [RelayCommand(CanExecute = nameof(IsOpen))]
    private void Dismiss() => Close();

    private async Task SuggestAsync(string query)
    {
        var found = await source.SuggestAsync(job, query, CancellationToken.None);

        if (!draft.EndsWith(query, StringComparison.Ordinal) || typed < 0)
        {
            return;
        }

        suggestions.Clear();

        foreach (var mention in found)
        {
            var row = suggestions.Count;
            suggestions.Add(new MentionRowViewModel(mention, () => Pick(row)));
        }

        IsOpen = suggestions.Count > 0;
        Select(0);
    }

    private void Close()
    {
        suggestions.Clear();
        selected = -1;
        IsOpen = false;
    }

    private void Select(int index)
    {
        if (suggestions.Count == 0)
        {
            return;
        }

        selected = (index + suggestions.Count) % suggestions.Count;

        for (var row = 0; row < suggestions.Count; row++)
        {
            suggestions[row].IsSelected = row == selected;
        }
    }

    [GeneratedRegex(@"(?:^|\s)@(?<query>[^\s@]*)$")]
    private static partial Regex Typed();
}

[INotifyPropertyChanged]
internal sealed partial class MentionRowViewModel(Mention mention, Action choose) : IMentionRowViewModel
{
    public Mention Mention { get; } = mention;

    public IRelayCommand ChooseCommand { get; } = new RelayCommand(choose);

    public string Label => Mention.Label;

    public string Detail => Mention.Detail;

    public MentionKind Kind => Mention.Kind;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
