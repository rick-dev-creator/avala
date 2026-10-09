using Avala.Agents.Contracts.Events;
using Avala.Canvas.Contracts;
using Avala.Jobs.Contracts;
using Avala.Workbench.Cards;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Conversation;

[INotifyPropertyChanged]
internal sealed partial class DesignComposerViewModel : IComposerViewModel
{
    [ObservableProperty]
    public partial string Draft { get; set; } = string.Empty;

    public JobStatus Status => JobStatus.Running;

    public string Error => string.Empty;

    public bool AcceptsMessages => false;

    public IAsyncRelayCommand SendCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask, () => false);

    public IAsyncRelayCommand InterruptCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand StopCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}

internal sealed class DesignPromptViewModel : IPromptViewModel
{
    public int Attempt => 1;

    public string Origin => "Instruction";

    public bool IsFromPerson => true;

    public string Text => "Fix JPY rounding in invoice totals: yen has no minor unit, so totals must round to whole yen, not to two decimals.";

    public string Outcome => "running";
}

internal sealed class DesignMessageViewModel(string text, bool isStreaming) : IMessageViewModel
{
    public DesignMessageViewModel()
        : this("Totals now round through the currency's minor units, so ¥1,234.5 becomes ¥1,235 while €12.345 still becomes €12.35. I am adding a test for", true)
    {
    }

    public string Text { get; } = text;

    public bool IsStreaming { get; } = isStreaming;
}

internal sealed class DesignReasoningViewModel : IReasoningViewModel
{
    public string Text => "Invoice.total() calls Math.round(amount * 100) / 100 for every currency. JPY has zero decimals in ISO 4217, so the fix belongs in Money.round, which already knows the currency.";

    public bool IsThinking => false;

    public TimeSpan Duration => TimeSpan.FromSeconds(12);

    public string Summary => "Thought for 12s";

    public bool IsExpanded => false;

    public IRelayCommand ToggleCommand { get; } = new RelayCommand(() => { });
}

internal sealed record DesignToolViewModel(ItemKind Kind, string Title, string Output) : IToolViewModel
{
    public DesignToolViewModel()
        : this(ItemKind.Command, "npm test -- money.test.ts", "PASS src/money/money.test.ts\n  ✓ rounds JPY to whole yen (3 ms)\n  ✓ rounds EUR to cents (1 ms)")
    {
    }

    public string Input => string.Empty;

    public bool IsRunning => false;

    public bool Failed => false;

    public string Outcome => "done";

    public bool IsExpanded => false;

    public IRelayCommand ToggleCommand { get; } = new RelayCommand(() => { });
}

internal sealed class DesignPlanViewModel : IPlanViewModel
{
    public IReadOnlyList<PlanStep> Steps { get; } =
    [
        new("Find where invoice totals are rounded", PlanStepStatus.Done),
        new("Round through the currency's minor units", PlanStepStatus.Done),
        new("Add JPY and EUR rounding tests", PlanStepStatus.InProgress),
        new("Run the invoice test suite", PlanStepStatus.Pending),
    ];

    public string Progress => "2 of 4";
}

internal sealed class DesignCanvasViewModel : ICanvasViewModel
{
    public string Title => "Rounding before and after";

    public string MediaType => "text/vnd.mermaid";

    public string Content => "flowchart LR\n  A[Line items] --> B[Sum in minor units]\n  B --> C{Currency}\n  C -->|JPY: 0 decimals| D[¥1,235]\n  C -->|EUR: 2 decimals| E[€12.35]";

    public CanvasStatus Status => CanvasStatus.Completed;

    public bool IsStreaming => false;
}

internal sealed class DesignTurnEndViewModel : ITurnEndViewModel
{
    public TurnOutcome Outcome => TurnOutcome.Finished;

    public TimeSpan Duration => TimeSpan.FromSeconds(154);

    public string Summary => "Worked for 2m 34s";

    public long Tokens => 48_210;

    public string Cost => "0.4120 USD";
}

internal sealed class DesignRestartViewModel : IRestartViewModel
{
    public string Note => "Avala restarted. The agent's work before this point is summarized by its attempts above.";
}

internal sealed class DesignConversationViewModel : IConversationViewModel
{
    public JobId Job => SampleJobs.JpyRounding;

    public string Title => "Fix JPY rounding in invoice totals";

    public JobStatus Status => JobStatus.Running;

    public string Plan => "2 of 4";

    public IComposerViewModel Composer { get; } = new DesignComposerViewModel();

    public IReadOnlyList<object> Entries { get; } =
    [
        new DesignPromptViewModel(),
        new DesignReasoningViewModel(),
        new DesignPlanViewModel(),
        new DesignToolViewModel(ItemKind.Search, "Search for Math.round in src/invoices", "src/invoices/invoice.ts:41"),
        new DesignToolViewModel(ItemKind.FileEdit, "Edit src/money/money.ts", "+ return round(amount, minorUnits(currency))"),
        new DesignCanvasViewModel(),
        new DesignPermissionCardViewModel(),
        new DesignToolViewModel(),
        new DesignMessageViewModel(),
    ];
}
