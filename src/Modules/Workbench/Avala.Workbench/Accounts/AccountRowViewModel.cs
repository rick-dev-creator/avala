using Avala.Components.Privacy;
using Avala.Forges.Contracts;
using Avala.Workbench.Machine;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Accounts;

internal interface IAccountRowViewModel
{
    string Name { get; }

    Personal Login { get; }

    string Host { get; }

    string SignIn { get; }

    string Logo { get; }

    AccountState State { get; }

    bool IsDetected { get; }

    string Problem { get; }

    IAsyncRelayCommand AddCommand { get; }

    IAsyncRelayCommand RemoveCommand { get; }
}

internal sealed class AccountRowViewModel(AccountStatus account, SourceControlState state, IAsyncRelayCommand add, IAsyncRelayCommand remove) : IAccountRowViewModel
{
    public AccountStatus Account { get; } = account;

    public string Name { get; } = account.Name.Value;

    public Personal Login { get; } = AccountPhrases.Login(account);

    public string Host { get; } = account.Host.Length > 0 ? account.Host : state.Kind(account.ForgeKind).Match(kind => kind.Name, () => account.ForgeKind);

    public string SignIn { get; } = AccountPhrases.SignIn(account, state.Tool(account.ForgeKind));

    public string Logo { get; } = state.Kind(account.ForgeKind).Match(kind => kind.Logo, () => string.Empty);

    public AccountState State { get; } =
        account.Origin == AccountOrigin.Detected ? AccountState.Detected
        : account.Problem.IsSome ? AccountState.Attention
        : AccountState.SignedIn;

    public bool IsDetected => State == AccountState.Detected;

    public string Problem { get; } = account.Problem.Match(AccountPhrases.Problem, () => string.Empty);

    public IAsyncRelayCommand AddCommand { get; } = add;

    public IAsyncRelayCommand RemoveCommand { get; } = remove;
}
