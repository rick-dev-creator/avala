using Avala.Resources.Contracts;
using Avala.Sdk;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Settings;

internal sealed class DesignWorktreeSettingsViewModel : IWorktreeSettingsViewModel
{
    private static readonly WorktreePolicy Policy =
        new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromDays(1), TimeSpan.Zero, TimeSpan.Zero) { DeleteDiscarded = TimeSpan.FromDays(7), DeleteFailed = TimeSpan.FromDays(30), Idle = TimeSpan.FromDays(3) };

    public IReadOnlyList<IWorktreeRuleViewModel> Moments { get; } =
        [.. WorktreePhrases.Shown.Select(moment => Rule(moment.Title, moment.Explanation, WorktreePhrases.FreeChoices(Policy.FreeAfter(moment.Moment)), Policy.FreeAfter(moment.Moment)))];

    public IReadOnlyList<IWorktreeRuleViewModel> Deletions { get; } =
    [
        Rule("A discarded job's work", WorktreePhrases.DeletionNote, WorktreePhrases.DeleteChoices(Policy.DeleteDiscarded), Policy.DeleteDiscarded),
        Rule("A failed job's work", WorktreePhrases.DeletionNote, WorktreePhrases.DeleteChoices(Policy.DeleteFailed), Policy.DeleteFailed),
    ];

    public IReadOnlyList<IWorktreeLimitViewModel> Limits { get; } =
    [
        new WorktreeLimitViewModel(new WorktreeLimit(WorktreePhrases.BudgetTitle, WorktreePhrases.BudgetNote, WorktreePhrases.BudgetChoices(20d), 20d), () => { }),
        new WorktreeLimitViewModel(new WorktreeLimit(WorktreePhrases.FloorTitle, WorktreePhrases.FloorNote, WorktreePhrases.FloorChoices(10d), 10d), () => { }),
    ];

    public string File { get; init; } = "resources.json: Absent";

    public string Error { get; init; } = string.Empty;

    public bool IsChanged { get; init; }

    public IAsyncRelayCommand SaveCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static WorktreeRuleViewModel Rule(string title, string explanation, IReadOnlyList<WorktreeChoice> choices, Option<TimeSpan> current) =>
        new(new WorktreeRule(title, explanation, choices, current), () => { });
}

internal sealed class DesignWorktreeRuleViewModel : IWorktreeRuleViewModel
{
    public string Title { get; init; } = "Archived";

    public string Explanation { get; init; } = "You archived its conversation.";

    public IReadOnlyList<string> Choices { get; } = [.. WorktreePhrases.FreeChoices(TimeSpan.Zero).Select(choice => choice.Label)];

    public string Choice { get; set; } = "Free it at once";
}

internal sealed class DesignWorktreeLimitViewModel : IWorktreeLimitViewModel
{
    public string Title { get; init; } = WorktreePhrases.BudgetTitle;

    public string Explanation { get; init; } = WorktreePhrases.BudgetNote;

    public IReadOnlyList<string> Choices { get; } = [.. WorktreePhrases.BudgetChoices(20d).Select(choice => choice.Label)];

    public string Choice { get; set; } = "20 GB";
}
