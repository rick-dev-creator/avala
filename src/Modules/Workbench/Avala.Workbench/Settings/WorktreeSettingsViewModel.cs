using Avala.Resources.Contracts;
using Avala.Sdk;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Settings;

internal interface IWorktreeSettingsViewModel
{
    IReadOnlyList<IWorktreeRuleViewModel> Moments { get; }

    IReadOnlyList<IWorktreeRuleViewModel> Deletions { get; }

    string File { get; }

    string Error { get; }

    bool IsChanged { get; }

    IAsyncRelayCommand SaveCommand { get; }

    Task LoadAsync(CancellationToken cancellationToken);
}

internal interface IWorktreeRuleViewModel
{
    string Title { get; }

    string Explanation { get; }

    IReadOnlyList<string> Choices { get; }

    string Choice { get; set; }
}

[INotifyPropertyChanged]
internal sealed partial class WorktreeSettingsViewModel(IResources resources, IUiDispatcher ui) : IWorktreeSettingsViewModel
{
    private const string DiscardedWork = "A discarded job's work";

    private const string FailedWork = "A failed job's work";

    private string saved = string.Empty;
    private bool showing;

    private IReadOnlyList<WorktreeRuleViewModel> moments = [];
    private IReadOnlyList<WorktreeRuleViewModel> deletions = [];

    public IReadOnlyList<IWorktreeRuleViewModel> Moments => moments;

    public IReadOnlyList<IWorktreeRuleViewModel> Deletions => deletions;

    [ObservableProperty]
    public partial string File { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Error { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsChanged { get; private set; }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var settings = await resources.SettingsAsync(cancellationToken);

        await ui.InvokeAsync(() => Show(settings), cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(IsChanged))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var changed = await resources.ChangeWorktreesAsync(Draft(), cancellationToken);
        Error = changed.Match(_ => string.Empty, WorktreePhrases.Refused);

        if (changed.TryGetValue(out var settings, out _))
        {
            Show(settings);
        }
    }

    private void Show(ResourceSettings settings)
    {
        var policy = settings.Worktrees;
        showing = true;
        moments = [.. WorktreePhrases.Shown.Select(moment => Rule(moment.Title, moment.Explanation, WorktreePhrases.FreeChoices(policy.FreeAfter(moment.Moment)), policy.FreeAfter(moment.Moment)))];
        deletions =
        [
            Rule(DiscardedWork, WorktreePhrases.DeletionNote, WorktreePhrases.DeleteChoices(policy.DeleteDiscarded), policy.DeleteDiscarded),
            Rule(FailedWork, WorktreePhrases.DeletionNote, WorktreePhrases.DeleteChoices(policy.DeleteFailed), policy.DeleteFailed),
        ];
        OnPropertyChanged(nameof(Moments));
        OnPropertyChanged(nameof(Deletions));
        File = WorktreePhrases.File(settings);
        showing = false;
        saved = WorktreePhrases.Key(Draft());
        Refresh();
    }

    private WorktreeRuleViewModel Rule(string title, string explanation, IReadOnlyList<WorktreeChoice> choices, Option<TimeSpan> current) =>
        new(new WorktreeRule(title, explanation, choices, current), Refresh);

    private WorktreePolicy Draft() =>
        new(
            Archived: moments[0].After,
            Delivered: moments[1].After,
            PullRequest: moments[2].After,
            Undelivered: moments[3].After,
            Discarded: moments[4].After,
            Failed: moments[5].After)
        {
            DeleteDiscarded = deletions[0].After,
            DeleteFailed = deletions[1].After,
        };

    private void Refresh()
    {
        if (showing || saved.Length == 0)
        {
            return;
        }

        IsChanged = WorktreePhrases.Key(Draft()) != saved;
    }
}

[INotifyPropertyChanged]
internal sealed partial class WorktreeRuleViewModel(WorktreeRule rule, Action changed) : IWorktreeRuleViewModel
{
    public string Title { get; } = rule.Title;

    public string Explanation { get; } = rule.Explanation;

    public IReadOnlyList<string> Choices { get; } = [.. rule.Choices.Select(choice => choice.Label)];

    [ObservableProperty]
    public partial string Choice { get; set; } = rule.Choices.First(choice => choice.After == rule.Current).Label;

    public Option<TimeSpan> After => rule.Choices.First(choice => choice.Label == Choice).After;

    partial void OnChoiceChanged(string value) => changed();
}
