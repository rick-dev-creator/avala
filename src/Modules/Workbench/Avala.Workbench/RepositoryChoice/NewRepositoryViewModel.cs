using Avala.Sdk;
using Avala.Workspaces.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.RepositoryChoice;

internal interface INewRepositoryViewModel
{
    bool IsOpen { get; }

    string Parent { get; }

    string Name { get; set; }

    string Error { get; }

    IAsyncRelayCommand StartCommand { get; }

    IAsyncRelayCommand CreateCommand { get; }

    IRelayCommand CancelCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class NewRepositoryViewModel(IFolderPicker picker, IRepositories repositories) : INewRepositoryViewModel
{
    public event EventHandler<string>? Created;

    public string Near { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    public partial string Parent { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Error { get; private set; } = string.Empty;

    [RelayCommand]
    private async Task StartAsync(CancellationToken cancellationToken)
    {
        var near = Near.Trim().TrimEnd('/', '\\');
        var chosen = await picker.PickAsync(RepositoryPhrases.ParentTitle, Path.GetDirectoryName(near) ?? near, cancellationToken);

        chosen.Match<Action>(found => () => Open(found), () => () => { })();
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync(CancellationToken cancellationToken)
    {
        var created = await repositories.CreateAsync(Parent, Name, cancellationToken);
        Error = created.Match(_ => string.Empty, RepositoryPhrases.Failure);

        if (created.TryGetValue(out var repository, out _))
        {
            Cancel();
            Created?.Invoke(this, repository);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        IsOpen = false;
        Name = string.Empty;
        Error = string.Empty;
    }

    private bool CanCreate() => IsOpen && !string.IsNullOrWhiteSpace(Name);

    private void Open(string parent)
    {
        Parent = parent;
        Error = string.Empty;
        IsOpen = true;
    }
}
