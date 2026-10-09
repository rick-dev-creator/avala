using Avala.Agents.Contracts.Events;
using Avala.Canvas.Contracts;
using Avala.Components.Canvases;
using Avala.Components.Status;
using Avala.Jobs.Contracts;
using Avala.Workbench.Cards;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Conversation;

[INotifyPropertyChanged]
internal sealed partial class DesignComposerViewModel : IComposerViewModel
{
    public DesignComposerViewModel()
        : this(JobStatus.Running)
    {
    }

    public DesignComposerViewModel(JobStatus status)
    {
        Status = status;
        Draft = string.Empty;
        SendCommand = new AsyncRelayCommand(() => Task.CompletedTask, () => AcceptsMessages && Draft.Length > 0);
        InterruptCommand = new AsyncRelayCommand(() => Task.CompletedTask, () => status == JobStatus.Running);
        StopCommand = new AsyncRelayCommand(() => Task.CompletedTask, () => status == JobStatus.Running);
    }

    [ObservableProperty]
    public partial string Draft { get; set; }

    public JobStatus Status { get; }

    public string Error => string.Empty;

    public string Placeholder => ConversationPhrases.Placeholder(Status);

    public bool AcceptsMessages => Status is JobStatus.NeedsHelp or JobStatus.AwaitingReview;

    public IAsyncRelayCommand SendCommand { get; }

    public IAsyncRelayCommand InterruptCommand { get; }

    public IAsyncRelayCommand StopCommand { get; }
}

internal sealed class DesignPromptViewModel(string text) : IPromptViewModel
{
    public DesignPromptViewModel()
        : this("TestInvoiceTotal_JPY has failed on main since the tax change. Fix it without changing the public API.")
    {
    }

    public int Attempt => 1;

    public string Origin => "Instruction";

    public bool IsFromPerson => true;

    public bool ShowsOrigin => false;

    public string Text { get; } = text;

    public string Outcome => "running";
}

internal sealed class DesignMessageViewModel(string text, bool isStreaming) : IMessageViewModel
{
    public DesignMessageViewModel()
        : this("I'm using the **ISO 4217 exponent** for each currency instead of a fixed 100, so yen amounts stay whole and tax is rounded exactly once. `Money.Total()` keeps its signature. Running the", true)
    {
    }

    public string Text { get; } = text;

    public bool IsStreaming { get; } = isStreaming;

    public string LinkNotice { get; init; } = string.Empty;

    public IAsyncRelayCommand<string> OpenLinkCommand { get; } = new AsyncRelayCommand<string>(_ => Task.CompletedTask);
}

internal sealed class DesignReasoningViewModel(string summary, bool isThinking) : IReasoningViewModel
{
    public DesignReasoningViewModel()
        : this("Thought for 6s", false)
    {
    }

    public string Text => "ISO 4217 gives JPY an exponent of 0, so the factor should be 10^exponent rather than a constant. Total() can look it up from the currency code it already has, which keeps the signature unchanged.";

    public bool IsThinking { get; } = isThinking;

    public TimeSpan Duration => TimeSpan.FromSeconds(6);

    public string Summary { get; } = summary;

    public bool IsExpanded => false;

    public bool HasText => true;

    public IRelayCommand ToggleCommand { get; } = new RelayCommand(() => { });
}

internal sealed record DesignToolViewModel(ItemKind Kind, string Title, string Output) : IToolViewModel
{
    public DesignToolViewModel()
        : this(ItemKind.Command, "go test ./internal/money/...", "--- FAIL: TestInvoiceTotal_JPY (0.00s)\n    invoice_test.go:41: want ¥1,080  got ¥1,100")
    {
        Failed = true;
        Outcome = "failed";
    }

    public string Input => string.Empty;

    public bool IsRunning { get; init; }

    public bool Failed { get; init; }

    public string Outcome { get; init; } = "done";

    public bool IsExpanded => false;

    public IRelayCommand ToggleCommand { get; } = new RelayCommand(() => { });
}

internal sealed class DesignPlanViewModel : IPlanViewModel
{
    public IReadOnlyList<PlanStep> Steps { get; } =
    [
        new("Reproduce the failure", PlanStepStatus.Done),
        new("Find where rounding happens", PlanStepStatus.Done),
        new("Use each currency's exponent", PlanStepStatus.InProgress),
        new("Re-run the money tests", PlanStepStatus.Pending),
    ];

    public string Progress => "2 of 4";
}

internal sealed class DesignCanvasViewModel : ICanvasViewModel
{
    public string Title => "Rounding path";

    public string MediaType => CanvasMediaTypes.Svg;

    public string Content => DesignCanvasSurfaceViewModel.RoundingPath;

    public CanvasStatus Status => CanvasStatus.Completed;

    public bool IsStreaming => false;

    public ICanvasSurfaceViewModel Surface { get; } = Drawn();

    private static CanvasSurfaceViewModel Drawn()
    {
        var surface = new CanvasSurfaceViewModel();
        surface.Show(new CanvasDraft("Rounding path", CanvasMediaTypes.Svg, DesignCanvasSurfaceViewModel.RoundingPath, CanvasPhase.Completed));

        return surface;
    }
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

internal sealed record DesignConversationViewModel(string Title, string Place, IStatusPillViewModel Pill, IComposerViewModel Composer, IReadOnlyList<object> Entries) : IConversationViewModel
{
    public DesignConversationViewModel()
        : this(
            "Fix JPY rounding in invoice totals",
            "billing-worker · claude-work",
            new StatusPillViewModel(StatusKind.Working, "Running"),
            new DesignComposerViewModel(),
            [
                new DesignPromptViewModel(),
                new DesignToolViewModel(),
                new DesignToolViewModel(ItemKind.Search, "ToMinor", "internal/money/minor.go:12\ninternal/money/total.go:30"),
                new DesignReasoningViewModel(),
                new DesignPlanViewModel(),
                new DesignMessageViewModel("JPY has no minor units, but ToMinor multiplies every currency by 100. Tax is rounded once in sen and then again in yen. Here's the path:", false),
                new DesignCanvasViewModel(),
                new DesignToolViewModel(ItemKind.FileEdit, "internal/money/minor.go", "+ factor := pow10(currency.Exponent())"),
                new DesignReasoningViewModel("Thought for 4s", false),
                new DesignMessageViewModel(),
            ])
    {
    }

    public static DesignConversationViewModel Asking =>
        new(
            "Fix flaky CheckoutForm test",
            "web-console · claude-personal",
            new StatusPillViewModel(StatusKind.NeedsYou, "Needs you"),
            new DesignComposerViewModel(JobStatus.Running),
            [
                new DesignPromptViewModel("CheckoutForm.test.tsx fails about one run in five on CI. Find out why and fix it."),
                new DesignToolViewModel(ItemKind.Command, "pnpm vitest run CheckoutForm --repeat 20", "4 of 20 failed") { Failed = true, Outcome = "failed" },
                new DesignToolViewModel(ItemKind.Search, "fireEvent.change", "9 results"),
                new DesignReasoningViewModel("Thought for 12s", false),
                new DesignMessageViewModel("The failures happen when fireEvent changes the card number while the validator is still debouncing. user-event awaits each keystroke, which removes the race. It isn't installed in this package yet.", false),
                new DesignPermissionCardViewModel(),
            ]);

    public static DesignConversationViewModel Questioning =>
        new(
            "Add invoice PDF endpoint",
            "ledger-api · claude-work",
            new StatusPillViewModel(StatusKind.NeedsYou, "Needs you"),
            new DesignComposerViewModel(JobStatus.Running),
            [
                new DesignPromptViewModel("Add GET /invoices/:id/pdf that returns the invoice as a PDF. Reuse the HTML template in templates/invoice.html."),
                new DesignToolViewModel(ItemKind.Search, "invoice.html", "2 results"),
                new DesignToolViewModel(ItemKind.FileEdit, "internal/http/routes.go", "+6"),
                new DesignMessageViewModel("The route is wired up. Before I write the handler I need one decision about storage.", false),
                new DesignFormCardViewModel(),
                new DesignReasoningViewModel("Thinking", true),
                new DesignTurnEndViewModel(),
            ]);

    public static DesignConversationViewModel Empty =>
        new(
            "Update zod to 3.23",
            "web-console · claude-work",
            new StatusPillViewModel(StatusKind.Working, "Starting"),
            new DesignComposerViewModel(JobStatus.Preparing),
            []);

    public JobId Job => SampleJobs.JpyRounding;

    public JobStatus Status => JobStatus.Running;

    public string Plan => "2 of 4";
}
