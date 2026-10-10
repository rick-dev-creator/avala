using Avala.Workbench.ModelChoices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.NewJob;

internal sealed record DesignConnectionOptionViewModel(string Name, string Reading, bool IsNearLimit) : IConnectionOptionViewModel
{
    public DesignConnectionOptionViewModel()
        : this("claude-work", "88% of the 5-hour window used", true)
    {
    }
}

[INotifyPropertyChanged]
internal sealed partial class DesignNewJobViewModel : INewJobViewModel
{
    public string Title => "New job";

    public IReadOnlyList<string> Repositories { get; } = ["~/code/shop-api", "~/code/shop-web"];

    public IReadOnlyList<string> Connections { get; } = [NewJobPhrases.Auto, "claude-work", "claude-personal"];

    public IReadOnlyList<IConnectionOptionViewModel> Options { get; } =
    [
        new DesignConnectionOptionViewModel(NewJobPhrases.Auto, string.Empty, false),
        new DesignConnectionOptionViewModel("claude-work", "88% of the 5-hour window used", true),
        new DesignConnectionOptionViewModel("claude-personal", "31% of the 5-hour window used", false),
    ];

    [ObservableProperty]
    public partial string Repository { get; set; } = "~/code/shop-api";

    [ObservableProperty]
    public partial string Instruction { get; set; } = "Add an invoice PDF endpoint that serves the rendered invoice with a cache header.";

    [ObservableProperty]
    public partial string Connection { get; set; } = NewJobPhrases.Auto;

    public string Route => "Auto → claude-personal · 31% of the 5-hour window used · the most capacity left";

    public bool IsRouteAttention => false;

    public IReadOnlyList<string> Autonomies { get; } = ["Repository's level: autonomous", NewJobPhrases.Supervised];

    [ObservableProperty]
    public partial string Autonomy { get; set; } = "Repository's level: autonomous";

    public string AutonomyNote => "Autonomous, as the repository's .avala/permissions.json declares: edits and commands inside the worktree run without asking, anything else is denied, forms are answered by policy.";

    public IModelPickerViewModel Models { get; } = new DesignModelPickerViewModel();

    public string Error => string.Empty;

    public string Submitted => "Submitted: Fix JPY rounding in invoice totals";

    public IAsyncRelayCommand SubmitCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}
