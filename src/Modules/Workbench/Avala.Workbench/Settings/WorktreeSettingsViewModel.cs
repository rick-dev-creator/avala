using Avala.Resources.Contracts;
using Avala.Sdk;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Settings;

internal interface IWorktreeSettingsViewModel
{
    IReadOnlyList<IWorktreeRuleViewModel> Moments { get; }

    IReadOnlyList<IWorktreeRuleViewModel> Deletions { get; }

    IReadOnlyList<IWorktreeLimitViewModel> Limits { get; }

    string File { get; }

    string Error { get; }

    bool IsChanged { get; }

    IAsyncRelayCommand SaveCommand { get; }

    Task LoadAsync(CancellationToken cancellationToken);
}

internal interface IWorktreeLimitViewModel
{
    string Title { get; }

    string Explanation { get; }

    IReadOnlyList<string> Choices { get; }

    string Choice { get; set; }
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
    private IReadOnlyList<WorktreeLimitViewModel> limits = [];

    public IReadOnlyList<IWorktreeRuleViewModel> Moments => moments;

    public IReadOnlyList<IWorktreeRuleViewModel> Deletions => deletions;

    public IReadOnlyList<IWorktreeLimitViewModel> Limits => limits;

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
        var budget = policy.SpaceBudget.Map(bytes => Math.Round(bytes / WorktreePhrases.Gigabyte, 3));
        var floor = policy.DiskFloor.Map(share => Math.Round(share * 100, 3));
        limits =
        [
            new(new WorktreeLimit(WorktreePhrases.BudgetTitle, WorktreePhrases.BudgetNote, WorktreePhrases.BudgetChoices(budget), budget), Refresh),
            new(new WorktreeLimit(WorktreePhrases.FloorTitle, WorktreePhrases.FloorNote, WorktreePhrases.FloorChoices(floor), floor), Refresh),
        ];
        OnPropertyChanged(nameof(Moments));
        OnPropertyChanged(nameof(Deletions));
        OnPropertyChanged(nameof(Limits));
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
            Idle = moments[6].After,
            SpaceBudget = limits[0].Value.Map(gigabytes => (long)Math.Round(gigabytes * WorktreePhrases.Gigabyte)),
            DiskFloor = limits[1].Value.Map(percent => percent / 100),
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

[INotifyPropertyChanged]
internal sealed partial class WorktreeLimitViewModel(WorktreeLimit limit, Action changed) : IWorktreeLimitViewModel
{
    public string Title { get; } = limit.Title;

    public string Explanation { get; } = limit.Explanation;

    public IReadOnlyList<string> Choices { get; } = [.. limit.Choices.Select(choice => choice.Label)];

    [ObservableProperty]
    public partial string Choice { get; set; } = limit.Choices.First(choice => WorktreePhrases.Same(choice.Value, limit.Current)).Label;

    public Option<double> Value => limit.Choices.First(choice => choice.Label == Choice).Value;

    partial void OnChoiceChanged(string value) => changed();
}
