using Avala.Agents.Contracts.Events;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Cards;

[INotifyPropertyChanged]
internal sealed partial class DesignFormChoiceViewModel(string label, string description, bool recommended) : IFormChoiceViewModel
{
    public DesignFormChoiceViewModel()
        : this("Render on each request", "No storage. About 300 ms per request.", true)
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
    public string Header => "Storage";

    public string Prompt => string.Empty;

    public FieldKind Kind => FieldKind.SingleChoice;

    public bool AcceptsText => false;

    public bool IsConfirmation => false;

    public IReadOnlyList<IFormChoiceViewModel> Choices { get; } =
    [
        new DesignFormChoiceViewModel(),
        new DesignFormChoiceViewModel("Cache in object storage for 24h", "Faster repeats. Adds a bucket and credentials.", false),
        new DesignFormChoiceViewModel("Store permanently with the invoice", "An immutable copy. Needs a migration.", false),
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

    public string Headline => "Asks a question";

    public string Title => "Where should generated PDFs live?";

    public string Context => "Invoices render to about 80 KB. Nothing in this service stores files today.";

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
    public string Title => "Install user-event so each keystroke is awaited";

    public ItemKind Kind => ItemKind.Command;

    public string Headline => "Wants to run a command";

    public string Target => "pnpm add -D @testing-library/user-event@14.5.2";

    public bool AwaitsYou => true;

    public string Verdict => "Waiting for you";

    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool DontAskAgain { get; set; }

    public string DontAskAgainLabel => CardPhrases.DontAskAgain;

    public string DontAskAgainScope => CardPhrases.DontAskAgainScope(Kind);

    public string Error => string.Empty;

    public IAsyncRelayCommand AllowCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand DenyCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}
