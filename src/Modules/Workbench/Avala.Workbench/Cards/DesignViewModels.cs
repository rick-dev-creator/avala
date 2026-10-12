using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
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

    public string Writes => string.Empty;

    public bool AwaitsYou => true;

    public string Verdict => "Waiting for you";

    public bool IsAllowed => false;

    public bool IsDenied => false;

    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    public IAnswerScopeViewModel Scope { get; } = new DesignAnswerScopeViewModel();

    public string Notice => string.Empty;

    public string Error => string.Empty;

    public IAsyncRelayCommand AllowCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand DenyCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}

internal sealed class DesignDeniedCardViewModel : IDeniedCardViewModel
{
    public string What { get; init; } = "Fetch https://registry.npmjs.org/@testing-library/user-event";

    public string Why => "Nobody was watching, so the policy denied it.";

    public bool IsAllowed { get; init; }

    public bool CanAllow => !IsAllowed;

    public string Notice => IsAllowed ? CardPhrases.AllowedAfterDenial(Option<JobRejection>.None) : string.Empty;

    public string Error => string.Empty;

    public IAsyncRelayCommand AllowCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}

internal sealed class DesignAnswerScopeViewModel : IAnswerScopeViewModel
{
    public AnswerScope Scope { get; init; } = AnswerScope.Always;

    public bool IsOnce => Scope == AnswerScope.Once;

    public bool IsAlways => Scope == AnswerScope.Always;

    public bool IsEverything => Scope == AnswerScope.Everything;

    public bool IsInRepository => Scope == AnswerScope.InRepository;

    public string AlwaysLabel { get; init; } = "Always pnpm in this job";

    public string EverythingLabel => ScopePhrases.Everything(contest: false);

    public string InRepositoryLabel => CardPhrases.AlwaysInRepository;

    public string InRepositoryHint =>
        CardPhrases.AlwaysInRepositoryScope(new(RuleOrigin.Repository, "always in this repository", ItemKind.Command, "pnpm add -D @testing-library/user-event@14.5.2", RuleScope.Anywhere, PolicyAnswer.Allow));

    public bool OffersInRepository => true;

    public string AlwaysUnavailable { get; init; } = string.Empty;

    public string EverythingUnavailable { get; init; } = string.Empty;

    public IRelayCommand<AnswerScope> ChooseCommand { get; } = new RelayCommand<AnswerScope>(_ => { });
}
