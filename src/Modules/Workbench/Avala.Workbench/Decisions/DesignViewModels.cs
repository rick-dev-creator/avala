using Avala.Jobs.Contracts;
using Avala.Workbench.Cards;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Decisions;

internal sealed class DesignDecisionViewModel(JobId job, string jobTitle, object card, string waiting) : IDecisionViewModel
{
    public DesignDecisionViewModel()
        : this(SampleJobs.FlakyCheckout, "Fix flaky CheckoutForm test", new DesignPermissionCardViewModel(), "waiting 4m")
    {
    }

    public JobId Job { get; } = job;

    public string JobTitle { get; } = jobTitle;

    public object Card { get; } = card;

    public string Waiting { get; } = waiting;
}

[INotifyPropertyChanged]
internal sealed partial class DesignDecisionsViewModel : IDecisionsViewModel
{
    public DesignDecisionsViewModel() => Selected = Items[0];

    public string Empty => "Nothing needs you";

    public IReadOnlyList<IDecisionViewModel> Items { get; } =
    [
        new DesignDecisionViewModel(),
        new DesignDecisionViewModel(SampleJobs.InvoicePdf, "Add invoice PDF endpoint", new DesignFormCardViewModel(), "waiting less than a minute"),
    ];

    public bool IsEmpty => false;

    [ObservableProperty]
    public partial IDecisionViewModel? Selected { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    public IRelayCommand MoveNextCommand { get; } = new RelayCommand(() => { });

    public IRelayCommand MovePreviousCommand { get; } = new RelayCommand(() => { });

    public IRelayCommand<string> ChooseCommand { get; } = new RelayCommand<string>(_ => { });

    public IAsyncRelayCommand AnswerCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand DenyCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

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
