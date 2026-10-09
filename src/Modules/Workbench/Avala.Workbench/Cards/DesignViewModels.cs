using Avala.Agents.Contracts.Events;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Cards;

[INotifyPropertyChanged]
internal sealed partial class DesignFormChoiceViewModel(string label, string description, bool recommended) : IFormChoiceViewModel
{
    public DesignFormChoiceViewModel()
        : this("GET /invoices/{id}/pdf", "A resource of its own, cached by the CDN like the other invoice routes.", true)
    {
    }

    public string Label { get; } = label;

    public string Description { get; } = description;

    public bool Recommended { get; } = recommended;

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = recommended;
}

[INotifyPropertyChanged]
internal sealed partial class DesignFormFieldViewModel : IFormFieldViewModel
{
    public string Header => "Endpoint";

    public string Prompt => "Where should the invoice PDF be served from?";

    public FieldKind Kind => FieldKind.SingleChoice;

    public bool AcceptsText => true;

    public bool IsConfirmation => false;

    public IReadOnlyList<IFormChoiceViewModel> Choices { get; } =
    [
        new DesignFormChoiceViewModel(),
        new DesignFormChoiceViewModel("GET /invoices/{id}?format=pdf", "Content negotiation on the existing route; no new route to document.", false),
    ];

    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool Confirmed { get; set; }

    public bool IsComplete => true;
}

[INotifyPropertyChanged]
internal sealed partial class DesignFormCardViewModel : IFormCardViewModel
{
    public FormPurpose Purpose => FormPurpose.Question;

    public string Title => "Add invoice PDF endpoint";

    public string Context => "The invoice service renders PDFs already; only the route is undecided.";

    public IReadOnlyList<IFormFieldViewModel> Fields { get; } = [new DesignFormFieldViewModel()];

    public bool AwaitsYou => true;

    public string Verdict => "Waiting for you";

    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    public string Error => string.Empty;

    public IAsyncRelayCommand SubmitCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand DeclineCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}

[INotifyPropertyChanged]
internal sealed partial class DesignPermissionCardViewModel : IPermissionCardViewModel
{
    public string Title => "Run the CheckoutForm tests";

    public ItemKind Kind => ItemKind.Command;

    public string Target => "npm test -- CheckoutForm.test.tsx --runInBand";

    public bool AwaitsYou => true;

    public string Verdict => "Waiting for you";

    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool DontAskAgain { get; set; }

    public string Error => string.Empty;

    public IAsyncRelayCommand AllowCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand DenyCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}
