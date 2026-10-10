using Avala.Triggers.Contracts;
using Avala.Workbench.Automation;

namespace Avala.Workbench.Triggers;

internal interface ITriggerItemViewModel
{
    TriggerId Id { get; }

    string Name { get; }

    string Repository { get; }

    string Fires { get; }

    string Target { get; }

    string Running { get; }

    string Next { get; }

    bool IsEnabled { get; }

    string Toggle { get; }

    IReadOnlyList<string> Runs { get; }
}

internal sealed class TriggerItemViewModel(TriggerView view, TimeZoneInfo zone) : ITriggerItemViewModel
{
    public TriggerId Id { get; } = view.State.Id;

    public string Name { get; } = view.State.Id.Name;

    public string Repository { get; } = view.State.Repository;

    public string Fires { get; } = TriggerPhrases.Fires(view.State);

    public string Target { get; } = TriggerPhrases.Target(view.State);

    public string Running { get; } = TriggerPhrases.Running(view.State);

    public string Next { get; } = TriggerPhrases.Next(view.State, zone);

    public bool IsEnabled { get; } = view.State.Enabled;

    public string Toggle { get; } = view.State.Enabled ? "Disable" : "Enable";

    public IReadOnlyList<string> Runs { get; } = [.. view.Runs.Select(run => TriggerPhrases.Run(run, zone))];
}
