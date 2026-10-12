using Avala.Sdk;
using Avala.Workspaces.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.RepositoryChoice;

internal interface ICloneRepositoryViewModel
{
    bool IsOpen { get; }

    string Url { get; set; }

    string Parent { get; }

    string Name { get; set; }

    bool IsCloning { get; }

    string Status { get; }

    string Error { get; }

    IAsyncRelayCommand StartCommand { get; }

    IAsyncRelayCommand ChooseParentCommand { get; }

    IAsyncRelayCommand CloneCommand { get; }

    IRelayCommand CancelCommand { get; }
}

internal sealed record RepositoryCloned(string Path, string Url);

[INotifyPropertyChanged]
internal sealed partial class CloneRepositoryViewModel(IFolderPicker picker, IRemoteRepositories remotes) : ICloneRepositoryViewModel
{
    private string suggested = string.Empty;

    public event EventHandler<RepositoryCloned>? Cloned;

    public string Near { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloneCommand))]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloneCommand))]
    public partial string Url { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloneCommand))]
    public partial string Parent { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloneCommand))]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloneCommand))]
    public partial bool IsCloning { get; private set; }

    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Error { get; private set; } = string.Empty;

    partial void OnUrlChanged(string value)
    {
        if (Name == suggested)
        {
            suggested = CloneNames.From(value);
            Name = suggested;
        }
    }

    [RelayCommand]
    private Task StartAsync(CancellationToken cancellationToken)
    {
        var near = Near.Trim().TrimEnd('/', '\\');
        Parent = near.Length > 0 ? Path.GetDirectoryName(near) ?? near : string.Empty;
        Error = string.Empty;
        Status = string.Empty;
        IsOpen = true;

        return Parent.Length > 0 ? Task.CompletedTask : ChooseParentAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task ChooseParentAsync(CancellationToken cancellationToken) =>
        (await picker.PickAsync(RepositoryPhrases.CloneParentTitle, Parent, cancellationToken)).Match<Action>(found => () => Parent = found, () => () => { })();

    [RelayCommand(CanExecute = nameof(CanClone))]
    private async Task CloneAsync(CancellationToken cancellationToken)
    {
        var url = Url.Trim();
        IsCloning = true;
        Error = string.Empty;
        Status = RepositoryPhrases.Cloning(url, Path.Combine(Parent, Name.Trim()));

        try
        {
            var cloned = await remotes.CloneAsync(url, Parent, Name, cancellationToken);
            Status = string.Empty;
            Error = cloned.Match(_ => string.Empty, RepositoryPhrases.Failure);

            if (cloned.TryGetValue(out var path, out _))
            {
                Close();
                Cloned?.Invoke(this, new RepositoryCloned(path, url));
            }
        }
        catch (OperationCanceledException)
        {
            Status = string.Empty;
            Error = RepositoryPhrases.CloneCancelled;
        }
        finally
        {
            IsCloning = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (IsCloning)
        {
            CloneCommand.Cancel();

            return;
        }

        Close();
    }

    private bool CanClone() => IsOpen && !IsCloning && Url.Trim().Length > 0 && Parent.Length > 0 && Name.Trim().Length > 0;

    private void Close()
    {
        IsOpen = false;
        Url = string.Empty;
        Name = string.Empty;
        suggested = string.Empty;
        Error = string.Empty;
    }
}

internal static class CloneNames
{
    private const string GitSuffix = ".git";

    public static string From(string url)
    {
        var trimmed = url.Trim().TrimEnd('/', '\\');
        var leaf = trimmed[(trimmed.LastIndexOfAny(['/', '\\', ':']) + 1)..];

        return leaf.EndsWith(GitSuffix, StringComparison.OrdinalIgnoreCase) ? leaf[..^GitSuffix.Length] : leaf;
    }
}
