using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.NewJob;

[INotifyPropertyChanged]
internal sealed partial class DesignNewJobViewModel : INewJobViewModel
{
    public string Title => "New job";

    public IReadOnlyList<string> Repositories { get; } = ["~/code/shop-api", "~/code/shop-web"];

    public IReadOnlyList<string> Connections { get; } = [NewJobViewModel.RepositoryDefault, "claude-work", "claude-personal"];

    [ObservableProperty]
    public partial string Repository { get; set; } = "~/code/shop-api";

    [ObservableProperty]
    public partial string Instruction { get; set; } = "Add an invoice PDF endpoint that serves the rendered invoice with a cache header.";

    [ObservableProperty]
    public partial string Connection { get; set; } = "claude-work";

    [ObservableProperty]
    public partial bool Supervised { get; set; }

    public string Error => string.Empty;

    public string Submitted => "Submitted: Fix JPY rounding in invoice totals";

    public IAsyncRelayCommand SubmitCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}
