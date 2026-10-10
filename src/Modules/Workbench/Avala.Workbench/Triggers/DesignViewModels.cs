using Avala.Triggers.Contracts;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Triggers;

internal sealed record DesignTriggerItemViewModel(string Name, string Repository, string Fires, string Target, string Running, string Next, bool IsEnabled, IReadOnlyList<string> Runs)
    : ITriggerItemViewModel
{
    public DesignTriggerItemViewModel()
        : this(
            "nightly-deps",
            "~/code/ledger-api",
            "Weekdays at 02:30",
            "Starts a job · supervised at most",
            "0 of 1 running",
            "Next · Mon 12 Oct 02:30",
            true,
            ["2026-10-09 02:30 · on schedule · job submitted", "2026-10-08 09:12 · caught up · job submitted"])
    {
    }

    public TriggerId Id => new("~/code/ledger-api", Name);

    public string Toggle => IsEnabled ? "Disable" : "Enable";
}

internal sealed class DesignTriggersViewModel : ITriggersViewModel
{
    public string Title => "Triggers";

    public string Scope => "Times in this computer's time zone, Europe/Madrid";

    public string Endpoint => "Listening on http://localhost:24010/hooks/<id>, on this computer only.";

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
            ["2026-10-09 15:08 · webhook · job submitted · capped to supervised by the repository"]),
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
        "2026-10-09 15:08:12 · /hooks/issue-opened · accepted",
        "2026-10-09 15:07:40 · /hooks/issue-opened · bad signature",
    ];

    public IAsyncRelayCommand<ITriggerItemViewModel> RunNowCommand { get; } = new AsyncRelayCommand<ITriggerItemViewModel>(_ => Task.CompletedTask);

    public IAsyncRelayCommand<ITriggerItemViewModel> ToggleCommand { get; } = new AsyncRelayCommand<ITriggerItemViewModel>(_ => Task.CompletedTask);

    public IAsyncRelayCommand ReloadCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}
