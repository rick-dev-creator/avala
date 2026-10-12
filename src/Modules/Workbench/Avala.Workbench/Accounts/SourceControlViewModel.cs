using System.Collections.ObjectModel;
using Avala.Forges.Contracts;
using Avala.Sdk;
using Avala.Workbench.Machine;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Accounts;

internal interface ISourceControlViewModel
{
    IReadOnlyList<IAccountRowViewModel> Accounts { get; }

    IConnectSheetViewModel Sheet { get; }

    bool IsChecking { get; }

    string Error { get; }

    IAsyncRelayCommand ConnectCommand { get; }

    IAsyncRelayCommand CheckAgainCommand { get; }

    Task LoadAsync(CancellationToken cancellationToken);
}

[INotifyPropertyChanged]
internal sealed partial class SourceControlViewModel : ISourceControlViewModel
{
    private readonly ObservableCollection<AccountRowViewModel> accounts = [];
    private readonly SourceControl source;
    private readonly ConnectSheetViewModel sheet;

    public SourceControlViewModel(SourceControl source, ConnectSheetViewModel sheet)
    {
        this.source = source;
        this.sheet = sheet;
        sheet.Connected += (_, _) => Loading = LoadAsync(CancellationToken.None);
    }

    public IReadOnlyList<IAccountRowViewModel> Accounts => accounts;

    public IConnectSheetViewModel Sheet => sheet;

    public ConnectSheetViewModel Connecting => sheet;

    public Task Loading { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    public partial bool IsChecking { get; private set; }

    [ObservableProperty]
    public partial string Error { get; private set; } = string.Empty;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsChecking = true;

        try
        {
            var state = await source.ReadAsync(cancellationToken);
            accounts.ShowOnly(state.Accounts.Accounts.Select(account => Row(account, state)));
        }
        finally
        {
            IsChecking = false;
        }
    }

    [RelayCommand]
    private Task ConnectAsync(CancellationToken cancellationToken) => sheet.OpenAsync(cancellationToken);

    [RelayCommand]
    private Task CheckAgainAsync(CancellationToken cancellationToken)
    {
        Loading = LoadAsync(cancellationToken);

        return Loading;
    }

    private AccountRowViewModel Row(AccountStatus account, SourceControlState state) =>
        new(
            account,
            state,
            new AsyncRelayCommand(token => AddAsync(account, state, token), () => account.Origin == AccountOrigin.Detected),
            new AsyncRelayCommand(token => ChangeAsync(source.RemoveAsync(account.Name, token)), () => account.Origin == AccountOrigin.Declared));

    private Task AddAsync(AccountStatus account, SourceControlState state, CancellationToken cancellationToken)
    {
        var defaultUrl = state.Kind(account.ForgeKind).Bind(kind => kind.DefaultUrl);

        return account.Url.IsNone && defaultUrl.IsNone
            ? sheet.OpenForAsync(account, cancellationToken)
            : ChangeAsync(source.ConnectAsync(SourceControl.Added(account, defaultUrl), cancellationToken));
    }

    private async Task ChangeAsync(ValueTask<Result<ForgeCatalog, ForgeError>> change)
    {
        var changed = await change;
        Error = changed.Match(_ => string.Empty, AccountPhrases.Refusal);
        await LoadAsync(CancellationToken.None);
    }
}
