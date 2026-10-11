using Avala.Triggers.Contracts;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Triggers;

internal sealed record DesignTriggerItemViewModel(string Name, string Repository, string Fires, string Target, string Running, string Next, bool IsEnabled, IReadOnlyList<string> Runs)
    : ITriggerItemViewModel
{
    public bool IsForge { get; init; }

    public DesignTriggerItemViewModel()
        : this(
            "nightly-deps",
            "~/code/ledger-api",
            "Weekdays at 02:30",
            "Starts a job · supervised at most",
            "0 of 1 running",
            "Next · Mon 12 Oct 02:30",
            true,
            ["Fri 9 Oct 02:30 · on schedule · job submitted", "Thu 8 Oct 09:12 · caught up · job submitted"])
    {
    }

    public TriggerId Id => new("~/code/ledger-api", Name);

    public string Toggle => IsEnabled ? "Disable" : "Enable";
}

internal sealed class DesignTriggersViewModel : ITriggersViewModel
{
    public string Title => "Triggers";

    public string Scope => "Times in this computer's time zone, Europe/Madrid";

    public string Endpoint => "Listening on this computer only, at";

    public string Url => "http://localhost:24010/hooks/<id>";

    public string Tunnel => TriggerPhrases.Tunnel;

    public string Notice => "Run now: job submitted.";

    public string Error => string.Empty;

    public IReadOnlyList<ITriggerItemViewModel> Triggers { get; } =
    [
        new DesignTriggerItemViewModel(),
        new DesignTriggerItemViewModel(
            "issue-opened",
            "~/code/web-console",
            "Webhook · /hooks/issue-opened",
            "Starts a job · autonomous at most",
            "1 of 2 running",
            "Runs on each signed delivery",
            true,
            ["Fri 9 Oct 15:08 · webhook · job submitted · capped to supervised by the repository"]),
        new DesignTriggerItemViewModel(
            "fix-labeled",
            "~/code/ledger-api",
            "GitHub · issue labeled `avala`",
            "Starts a job · autonomous at most",
            "1 of 2 running",
            "Next poll · 14:05",
            true,
            ["Fri 9 Oct 14:03 · forge github · alice · job submitted"])
        {
            IsForge = true,
        },
        new DesignTriggerItemViewModel("docs-sweep", "~/code/ledger-api", "Every 12 hours", "Queues a loop task · supervised at most", "0 of 1 running", "Disabled", false, []),
    ];

    public IReadOnlyList<string> Files { get; } =
    [
        "~/.local/share/Avala/triggers.json · applied, 1 trigger",
        "~/code/ledger-api/.avala/triggers.json · applied, 2 triggers",
        "~/code/web-console/.avala/triggers.json · rejected: InvalidTime",
    ];

    public IReadOnlyList<string> Deliveries { get; } =
    [
        "Fri 9 Oct 15:08 · /hooks/issue-opened · accepted",
        "Fri 9 Oct 15:07 · /hooks/issue-opened · bad signature",
    ];

    public IReadOnlyList<string> ForgeEvents { get; } =
    [
        "Fri 9 Oct 14:03 · fix-labeled · #42 labeled avala by alice · fired, job submitted",
        "Fri 9 Oct 14:02 · fix-labeled · #41 labeled avala by mallory · not allowed: not on the authors list",
        "Fri 9 Oct 14:02 · fix-labeled · #40 labeled bug by bob · filtered: another label",
    ];

    public bool HasForgeTriggers => true;

    public ILoopsViewModel Loops { get; } = new DesignLoopsViewModel();

    public IAsyncRelayCommand<ITriggerItemViewModel> RunNowCommand { get; } = new AsyncRelayCommand<ITriggerItemViewModel>(_ => Task.CompletedTask);

    public IAsyncRelayCommand<ITriggerItemViewModel> CheckNowCommand { get; } = new AsyncRelayCommand<ITriggerItemViewModel>(_ => Task.CompletedTask);

    public IAsyncRelayCommand<ITriggerItemViewModel> ToggleCommand { get; } = new AsyncRelayCommand<ITriggerItemViewModel>(_ => Task.CompletedTask);

    public IAsyncRelayCommand ReloadCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}
