using System.Collections.ObjectModel;
using Avala.Forges.Contracts;
using Avala.Sdk;
using Avala.Workbench.Accounts;
using Avala.Workbench.Machine;
using Avala.Workbench.Presenting;
using Avala.Workbench.Submitting;
using Avala.Workspaces.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.RepositoryChoice;

internal interface ICloneSheetViewModel
{
    bool IsOpen { get; }

    string Search { get; set; }

    IReadOnlyList<ICloneSourceViewModel> Sources { get; }

    IReadOnlyList<IRemoteRowViewModel> Repositories { get; }

    string Address { get; }

    bool IsLoading { get; }

    bool IsCloning { get; }

    string Status { get; }

    string Destination { get; }

    string Error { get; }

    string FixLabel { get; }

    bool IsFixable { get; }

    bool IsFailure { get; }

    IConnectSheetViewModel Connect { get; }

    IAsyncRelayCommand StartCommand { get; }

    IAsyncRelayCommand CloneAddressCommand { get; }

    IAsyncRelayCommand ChangeDestinationCommand { get; }

    IAsyncRelayCommand FixCommand { get; }

    IRelayCommand CancelCommand { get; }
}

internal sealed record RepositoryCloned(string Path, string Url);

internal sealed record CloneServices(SourceControl Source, IRemoteRepositories Remotes, ProjectsPlace Projects, TimeProvider Clock);

[INotifyPropertyChanged]
internal sealed partial class CloneSheetViewModel(CloneServices services, ConnectSheetViewModel connect) : ICloneSheetViewModel
{
    private readonly ObservableCollection<CloneSourceViewModel> sources = [];
    private readonly ObservableCollection<RemoteRowViewModel> repositories = [];
    private IReadOnlyList<RemoteRepository> listed = [];
    private Func<CancellationToken, Task> fix = _ => Task.CompletedTask;
    private int loads;

    public event EventHandler<RepositoryCloned>? Cloned;

    public IReadOnlyList<ICloneSourceViewModel> Sources => sources;

    public IReadOnlyList<IRemoteRowViewModel> Repositories => repositories;

    public IConnectSheetViewModel Connect => connect;

    public Task Loading { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloneAddressCommand))]
    public partial string Search { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloneAddressCommand))]
    public partial string Address { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloneAddressCommand))]
    public partial bool IsCloning { get; private set; }

    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Destination { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFixable), nameof(IsFailure))]
    public partial string Error { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFixable), nameof(IsFailure))]
    public partial string FixLabel { get; private set; } = string.Empty;

    public bool IsFixable => FixLabel.Length > 0;

    public bool IsFailure => Error.Length > 0 && FixLabel.Length == 0;

    partial void OnSearchChanged(string value)
    {
        Address = ClonePhrases.IsAddress(value) ? value.Trim() : string.Empty;
        ShowRows();
    }

    [RelayCommand]
    private async Task StartAsync(CancellationToken cancellationToken)
    {
        connect.Connected -= OnConnected;
        connect.Connected += OnConnected;
        Search = string.Empty;
        Failed(string.Empty, string.Empty, _ => Task.CompletedTask);
        IsOpen = true;
        Destination = (await services.Projects.ReadAsync(cancellationToken)).Folder;
        Loading = LoadSourcesAsync(Option<ForgeName>.None, cancellationToken);
        await Loading;
    }

    [RelayCommand(CanExecute = nameof(CanCloneAddress))]
    private Task CloneAddressAsync(CancellationToken cancellationToken) => CloneAsync(Address, CloneNames.From(Address), cancellationToken);

    [RelayCommand]
    private async Task ChangeDestinationAsync(CancellationToken cancellationToken) =>
        Destination = (await services.Projects.ChangeAsync(cancellationToken)).Match(found => found, () => Destination);

    [RelayCommand]
    private Task FixAsync(CancellationToken cancellationToken) => fix(cancellationToken);

    [RelayCommand]
    private void Cancel()
    {
        if (IsCloning)
        {
            CloneAddressCommand.Cancel();

            foreach (var row in repositories)
            {
                row.CloneCommand.Cancel();
            }

            return;
        }

        IsOpen = false;
        connect.Connected -= OnConnected;
    }

    private bool CanCloneAddress() => IsOpen && !IsCloning && Address.Length > 0 && CloneNames.From(Address).Length > 0;

    private void OnConnected(object? sender, ForgeName connected) => Loading = LoadSourcesAsync(connected, CancellationToken.None);

    private async Task LoadSourcesAsync(Option<ForgeName> wanted, CancellationToken cancellationToken)
    {
        var state = await services.Source.ReadAsync(cancellationToken);
        var usable = state.Accounts.Accounts.Where(account => account.Origin == AccountOrigin.Declared).ToList();
        sources.ShowOnly(usable.Select(account => new CloneSourceViewModel(account, state.Kind(account.ForgeKind).Match(kind => kind.Logo, () => string.Empty), forge => Loading = SelectAsync(forge, CancellationToken.None))));

        if (sources.Count == 0)
        {
            Failed(string.Empty, ClonePhrases.Connect, OpenConnectAsync);
            repositories.Clear();

            return;
        }

        var first = usable.FirstOrDefault(account => account.Problem.IsNone) ?? usable[0];
        await SelectAsync(wanted.Match(name => sources.Any(source => source.Forge == name) ? name : first.Name, () => first.Name), cancellationToken);
    }

    private async Task SelectAsync(ForgeName forge, CancellationToken cancellationToken)
    {
        var ticket = ++loads;

        foreach (var source in sources)
        {
            source.IsSelected = source.Forge == forge;
        }

        IsLoading = true;
        Failed(string.Empty, string.Empty, _ => Task.CompletedTask);
        var found = await services.Source.RepositoriesAsync(forge, string.Empty, cancellationToken);

        if (ticket != loads)
        {
            return;
        }

        IsLoading = false;
        listed = found.Match(list => list, _ => []);
        _ = found.Match(
            _ => true,
            error =>
            {
                Failed(ClonePhrases.Listing(error), ClonePhrases.Fix(error), ClonePhrases.Fix(error) == ClonePhrases.Connect ? OpenConnectAsync : token => SelectAsync(forge, token));

                return false;
            });
        ShowRows();
    }

    private void ShowRows()
    {
        var wanted = Search.Trim();
        var now = services.Clock.GetUtcNow();
        repositories.ShowOnly(Address.Length > 0 ? [] : listed
            .Where(found => wanted.Length == 0 || $"{found.Address.Owner}/{found.Address.Name}".Contains(wanted, StringComparison.OrdinalIgnoreCase))
            .Select(found => new RemoteRowViewModel(found, now, new AsyncRelayCommand(token => CloneAsync(found.CloneUrl, found.Address.Name, token), () => !IsCloning))));
    }

    private async Task CloneAsync(string url, string name, CancellationToken cancellationToken)
    {
        var folder = await services.Projects.EnsureAsync(cancellationToken);

        if (!folder.Match(found => true, () => false))
        {
            return;
        }

        var parent = folder.Match(found => found, () => Destination);
        Destination = parent;
        IsCloning = true;
        Failed(string.Empty, string.Empty, _ => Task.CompletedTask);
        Status = ClonePhrases.Cloning(name);

        try
        {
            var cloned = await services.Remotes.CloneAsync(url, parent, name, cancellationToken);

            if (cloned.TryGetValue(out var path, out var failure))
            {
                IsOpen = false;
                connect.Connected -= OnConnected;
                Cloned?.Invoke(this, new RepositoryCloned(path, url));
            }
            else
            {
                Failed(RepositoryPhrases.Failure(failure), ClonePhrases.Retry, token => CloneAsync(url, name, token));
            }
        }
        catch (OperationCanceledException)
        {
            Failed(RepositoryPhrases.CloneCancelled, string.Empty, _ => Task.CompletedTask);
        }
        finally
        {
            Status = string.Empty;
            IsCloning = false;
        }
    }

    private Task OpenConnectAsync(CancellationToken cancellationToken) => connect.OpenAsync(cancellationToken);

    private void Failed(string error, string label, Func<CancellationToken, Task> action)
    {
        Error = error;
        FixLabel = label;
        fix = action;
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
