using System.Collections.ObjectModel;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Machine;
using Avala.Workbench.NewJob;
using Avala.Workbench.Presenting;
using Avala.Workbench.Submitting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Welcome;

internal interface IWelcomeViewModel
{
    bool IsCompact { get; }

    bool CanCompose { get; }

    string Instruction { get; set; }

    string Error { get; }

    IReadOnlyList<IAccountChipViewModel> Chips { get; }

    IAsyncRelayCommand StartCommand { get; }

    IAsyncRelayCommand OpenFolderCommand { get; }

    IAsyncRelayCommand CloneCommand { get; }

    IAsyncRelayCommand NewRepositoryCommand { get; }

    IAsyncRelayCommand ConnectCommand { get; }

    void ShowBoard(bool hasJobs);

    Task CheckAsync(CancellationToken cancellationToken);
}

internal enum RepositoryStart
{
    OpenFolder,
    Clone,
    NewRepository,
}

internal interface IWelcomeLinks
{
    void Select(JobId job);

    Task StartAsync(RepositoryStart start);

    Task ConnectAsync();

    void OpenSettings();
}

[INotifyPropertyChanged]
internal sealed partial class WelcomeViewModel(JobLaunch launch, SourceControl source, IWelcomeLinks links) : IWelcomeViewModel
{
    private readonly ObservableCollection<AccountChipViewModel> chips = [];

    public IReadOnlyList<IAccountChipViewModel> Chips => chips;

    [ObservableProperty]
    public partial bool IsCompact { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial bool CanCompose { get; private set; } = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string Instruction { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Error { get; private set; } = string.Empty;

    public void ShowBoard(bool hasJobs) => IsCompact = hasJobs;

    public async Task CheckAsync(CancellationToken cancellationToken) => Show(await launch.CatalogAsync(cancellationToken), await source.ReadAsync(cancellationToken));

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync(CancellationToken cancellationToken)
    {
        var instruction = Instruction.Trim();

        if (!(await launch.ScratchAsync(instruction, cancellationToken)).TryGetValue(out var scratch, out var failure))
        {
            Error = NewJobPhrases.NoScratch(failure);

            return;
        }

        var started = await launch.SubmitAsync(scratch, instruction, Option<ConnectionName>.None, supervised: false, ModelChoice.Default, cancellationToken);
        Error = started.Match(_ => string.Empty, NewJobPhrases.Rejection);

        if (started.TryGetValue(out var job, out _))
        {
            Instruction = string.Empty;
            links.Select(job);
        }
    }

    [RelayCommand]
    private Task OpenFolderAsync() => links.StartAsync(RepositoryStart.OpenFolder);

    [RelayCommand]
    private Task CloneAsync() => links.StartAsync(RepositoryStart.Clone);

    [RelayCommand]
    private Task NewRepositoryAsync() => links.StartAsync(RepositoryStart.NewRepository);

    [RelayCommand]
    private Task ConnectAsync() => links.ConnectAsync();

    private bool CanStart() => CanCompose && Instruction.Trim().Length > 0;

    private void Show(ConnectionCatalog catalog, SourceControlState state)
    {
        CanCompose = catalog.File != ConnectionFileStatus.Rejected && catalog.Connections.Count > 0;
        chips.ShowOnly([.. Agents(catalog), .. Forges(state)]);
    }

    private IEnumerable<AccountChipViewModel> Agents(ConnectionCatalog catalog)
    {
        if (catalog.File == ConnectionFileStatus.Rejected)
        {
            return [AccountChipViewModel.Agent(WelcomePhrases.ConnectionsFile, attention: true, WelcomePhrases.OpenSettings, new AsyncRelayCommand(OpenedAsync))];
        }

        if (catalog.Connections.Count == 0)
        {
            return [AccountChipViewModel.Agent(WelcomePhrases.NoAgent, attention: true, WelcomePhrases.CheckAgain, new AsyncRelayCommand(RediscoverAsync))];
        }

        return catalog.Connections.Select(connection => AccountChipViewModel.Agent(connection.Name.Value, connection.Problem.IsSome, string.Empty, new AsyncRelayCommand(() => Task.CompletedTask)));
    }

    private IEnumerable<AccountChipViewModel> Forges(SourceControlState state) =>
        state.Accounts.Accounts
            .Where(account => account.Origin == AccountOrigin.Declared)
            .Select(account => AccountChipViewModel.Forge(
                account.Account.Match(found => found.Login, () => account.Name.Value),
                state.Kind(account.ForgeKind).Match(kind => kind.Logo, () => string.Empty),
                account.Problem.IsSome,
                account.Problem.IsSome ? WelcomePhrases.CheckAgain : string.Empty,
                new AsyncRelayCommand(token => CheckAsync(token))));

    private Task OpenedAsync()
    {
        links.OpenSettings();

        return Task.CompletedTask;
    }

    private async Task RediscoverAsync(CancellationToken cancellationToken) =>
        Show(await launch.RediscoverAsync(cancellationToken), await source.ReadAsync(cancellationToken));
}
