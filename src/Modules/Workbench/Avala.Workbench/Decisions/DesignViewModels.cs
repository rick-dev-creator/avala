using Avala.Components.Keycaps;
using Avala.Jobs.Contracts;
using Avala.Workbench.Cards;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Decisions;

internal sealed class DesignDecisionViewModel : IDecisionViewModel
{
    public DesignDecisionViewModel()
    {
        Job = SampleJobs.InvoicePdf;
        JobTitle = "Add invoice PDF endpoint";
        Title = "Where should generated PDFs live?";
        Asking = "asks a question";
        Card = new DesignFormCardViewModel();
        Context = "Invoices render to about 80 KB. Nothing in this service stores files today.";
        Options =
        [
            new(1, new DesignFormChoiceViewModel("Render on each request", string.Empty, true)),
            new(2, new DesignFormChoiceViewModel("Cache in object storage for 24h", string.Empty, false)),
            new(3, new DesignFormChoiceViewModel("Store permanently with the invoice", string.Empty, false)),
        ];
        Waiting = "6m";
        IsSelected = true;
    }

    public static DesignDecisionViewModel Permission(JobId job, string jobTitle, string title, string target, string waiting, bool isSelected = false) =>
        new()
        {
            Job = job,
            JobTitle = jobTitle,
            Title = title,
            Asking = "wants to run a command",
            Card = new DesignPermissionCardViewModel(),
            Context = string.Empty,
            Options = [],
            Target = target,
            Waiting = waiting,
            IsSelected = isSelected,
        };

    public JobId Job { get; init; }

    public string JobTitle { get; init; }

    public string Title { get; init; }

    public string Asking { get; init; }

    public object Card { get; init; }

    public bool IsPermission => Card is IPermissionCardViewModel;

    public string Target { get; init; } = string.Empty;

    public string Context { get; init; }

    public IReadOnlyList<DecisionOption> Options { get; init; }

    public bool IsSingleChoice => true;

    public string Waiting { get; init; }

    public bool IsSelected { get; init; }

    public IAsyncRelayCommand AnswerCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand DenyCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IRelayCommand OpenCommand { get; } = new RelayCommand(() => { });
}

[INotifyPropertyChanged]
internal sealed partial class DesignDecisionsViewModel : IDecisionsViewModel
{
    public DesignDecisionsViewModel()
        : this(
        [
            new DesignDecisionViewModel(),
            DesignDecisionViewModel.Permission(SampleJobs.FlakyCheckout, "Fix flaky CheckoutForm test", "Run pnpm add -D @testing-library/user-event", "pnpm add -D @testing-library/user-event@14.5.2", "2m"),
            DesignDecisionViewModel.Permission(SampleJobs.CheckoutSplit, "Rewrite webhook tests", "Run go generate ./internal/webhooks/...", "go generate ./internal/webhooks/...", "1m"),
        ])
    {
    }

    public DesignDecisionsViewModel(IReadOnlyList<IDecisionViewModel> items)
    {
        Items = items;
        Selected = items.FirstOrDefault(item => item.IsSelected);
        Hints = Selected is null ? DecisionHints.Idle : DecisionHints.For(Selected);
    }

    public event EventHandler? CloseRequested
    {
        add { }
        remove { }
    }

    public string Empty => "Nothing needs you";

    public string Pending => Items.Count == 0 ? "all answered" : $"{Items.Count} pending";

    public IReadOnlyList<IDecisionViewModel> Items { get; }

    public bool IsEmpty => Items.Count == 0;

    [ObservableProperty]
    public partial IDecisionViewModel? Selected { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    public bool IsWritingNote { get; init; }

    public IReadOnlyList<IKeycapHintViewModel> Hints { get; }

    public IKeycapHintViewModel CloseHint => DecisionHints.Close;

    public IRelayCommand MoveNextCommand { get; } = new RelayCommand(() => { });

    public IRelayCommand MovePreviousCommand { get; } = new RelayCommand(() => { });

    public IRelayCommand<string> ChooseCommand { get; } = new RelayCommand<string>(_ => { });

    public IAsyncRelayCommand AnswerCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand DenyCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IRelayCommand WriteNoteCommand { get; } = new RelayCommand(() => { });

    public IRelayCommand CloseCommand { get; } = new RelayCommand(() => { });

    public void Activate()
    {
    }

    public void Deactivate()
    {
    }

    public void Refresh()
    {
    }
}
