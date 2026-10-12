using System.Collections.ObjectModel;
using Avala.Forges.Contracts;
using Avala.Sdk;
using Avala.Workbench.Machine;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Accounts;

internal interface IForgeKindViewModel
{
    string Id { get; }

    string Name { get; }

    string Logo { get; }

    bool IsSelected { get; }

    IRelayCommand SelectCommand { get; }
}

internal interface IConnectSheetViewModel
{
    bool IsOpen { get; }

    IReadOnlyList<IForgeKindViewModel> Kinds { get; }

    bool NeedsHost { get; }

    string Host { get; set; }

    bool OffersCli { get; }

    bool UsesCli { get; set; }

    bool UsesToken { get; set; }

    string Cli { get; }

    CliState CliState { get; }

    string CliStatus { get; }

    string SignInCommand { get; }

    string TokenVariable { get; set; }

    string Error { get; }

    IAsyncRelayCommand ConnectCommand { get; }

    IAsyncRelayCommand CheckAgainCommand { get; }

    IRelayCommand CancelCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ForgeKindViewModel(ForgeInfo info, Action<string> select) : IForgeKindViewModel
{
    public ForgeInfo Info { get; } = info;

    public string Id => Info.Id;

    public string Name => Info.Name;

    public string Logo => Info.Logo;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Select() => select(Id);
}

[INotifyPropertyChanged]
internal sealed partial class ConnectSheetViewModel(SourceControl source) : IConnectSheetViewModel
{
    private readonly ObservableCollection<ForgeKindViewModel> kinds = [];
    private SourceControlState state = SourceControlState.None;
    private Option<string> reference;

    public event EventHandler<ForgeName>? Connected;

    public IReadOnlyList<IForgeKindViewModel> Kinds => kinds;

    public Task Reading { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    public partial string Kind { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial bool NeedsHost { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial string Host { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool OffersCli { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesToken))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial bool UsesCli { get; set; }

    public bool UsesToken
    {
        get => !UsesCli;
        set => UsesCli = !value;
    }

    [ObservableProperty]
    public partial string Cli { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial CliState CliState { get; private set; }

    [ObservableProperty]
    public partial string CliStatus { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string SignInCommand { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial string TokenVariable { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Error { get; private set; } = string.Empty;

    public Task OpenAsync(CancellationToken cancellationToken)
    {
        Error = string.Empty;
        IsOpen = true;
        Reading = ReadAsync(cancellationToken);

        return Reading;
    }

    public async Task OpenForAsync(AccountStatus account, CancellationToken cancellationToken)
    {
        await OpenAsync(cancellationToken);
        Select(account.ForgeKind);

        if (account.Source == CredentialSource.Environment)
        {
            UsesToken = true;
            TokenVariable = account.Reference.Match(variable => variable, () => TokenVariable);
        }
    }

    partial void OnHostChanged(string value) => ShowCli();

    [RelayCommand]
    private Task CheckAgainAsync(CancellationToken cancellationToken)
    {
        Reading = ReadAsync(cancellationToken);

        return Reading;
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var name = Name();
        var edit = new ForgeConnectionEdit(name, Kind)
        {
            Url = NeedsHost ? Address() : Option<Uri>.None,
            Source = UsesCli ? CredentialSource.Cli : CredentialSource.Environment,
            Reference = UsesCli ? reference : TokenVariable.Trim(),
        };
        var connected = await source.ConnectAsync(edit, cancellationToken);
        Error = connected.Match(_ => string.Empty, AccountPhrases.Refusal);

        if (connected.IsSuccess)
        {
            IsOpen = false;
            Connected?.Invoke(this, name);
        }
    }

    [RelayCommand]
    private void Cancel() => IsOpen = false;

    private bool CanConnect() =>
        IsOpen
        && Kind.Length > 0
        && (!NeedsHost || Address().IsSome)
        && (UsesCli ? CliState == CliState.SignedIn : TokenVariable.Trim().Length > 0);

    private async Task ReadAsync(CancellationToken cancellationToken)
    {
        state = await source.ReadAsync(cancellationToken);
        IReadOnlyList<ForgeInfo> ordered = [.. state.Kinds.OrderBy(kind => state.Tool(kind.Id).IsSome ? 0 : 1)];
        kinds.Reconcile(ordered, kind => kind.Id, kind => kind.Id, kind => new ForgeKindViewModel(kind, Select), (_, _) => { });
        Select(kinds.Any(kind => kind.Id == Kind) ? Kind : kinds.FirstOrDefault()?.Id ?? string.Empty);
    }

    private void Select(string id)
    {
        var changed = id != Kind;
        Kind = id;

        foreach (var kind in kinds)
        {
            kind.IsSelected = kind.Id == id;
        }

        var info = state.Kind(id);
        NeedsHost = info.Match(found => found.DefaultUrl.IsNone, () => false);

        if (changed || Host.Length == 0)
        {
            Host = NeedsHost ? Signed(id).Bind(account => account.Url).Match(url => url.GetLeftPart(UriPartial.Authority), () => info.Bind(found => found.SuggestedUrl).Match(url => url.OriginalString, () => string.Empty)) : string.Empty;
            TokenVariable = Tokens(id).FirstOrDefault() ?? AccountPhrases.TokenVariable(id);
        }

        var tool = state.Tool(id);
        OffersCli = tool.IsSome;
        Cli = tool.Match(found => found.Command, () => string.Empty);
        SignInCommand = tool.Bind(found => found.SignIn).Match(command => command, () => string.Empty);
        ShowCli();
        UsesCli = OffersCli && CliState != CliState.Missing && (CliState == CliState.SignedIn || Tokens(id).Count == 0);
    }

    private void ShowCli()
    {
        var tool = state.Tool(Kind);
        var signed = SignedHere();
        reference = signed.Bind(account => account.Reference);
        CliState = tool.Match(
            found => !found.Installed ? CliState.Missing : signed.IsSome ? CliState.SignedIn : CliState.SignedOut,
            () => CliState.None);
        CliStatus = AccountPhrases.Cli(CliState, Cli, signed.Bind(account => account.Account).Match(account => account.Login, () => string.Empty));
    }

    private Option<AccountStatus> SignedHere() =>
        state.Accounts.Accounts
            .Where(account => account.ForgeKind == Kind && account.Source == CredentialSource.Cli && account.Account.IsSome && account.Problem.IsNone)
            .FirstOrDefault(account => NeedsHost
                ? Address().Match(address => account.Url.Match(url => string.Equals(url.Host, address.Host, StringComparison.OrdinalIgnoreCase), () => false), () => false)
                : account.Url == state.Kind(Kind).Bind(kind => kind.DefaultUrl))
            .ToOption();

    private Option<AccountStatus> Signed(string id) =>
        state.Accounts.Accounts.FirstOrDefault(account => account.ForgeKind == id && account.Source == CredentialSource.Cli && account.Account.IsSome).ToOption();

    private List<string> Tokens(string id) =>
        [.. state.Accounts.Accounts.Where(account => account.ForgeKind == id && account.Source == CredentialSource.Environment).SelectMany(account => account.Reference.Match<IEnumerable<string>>(variable => [variable], () => []))];

    private Option<Uri> Address()
    {
        var typed = Host.Trim();
        var full = typed.Contains("://", StringComparison.Ordinal) ? typed : $"https://{typed}";

        return typed.Length > 0 && Uri.TryCreate(full, UriKind.Absolute, out var address) && address.Scheme is "https" or "http" && address.Host.Length > 0
            ? address
            : Option<Uri>.None;
    }

    private ForgeName Name()
    {
        var stem = NeedsHost ? Address().Match(address => Label(address.Host), () => Kind) : Kind;
        var taken = state.Accounts.Accounts.Where(account => account.Origin == AccountOrigin.Declared).Select(account => account.Name.Value).ToHashSet(StringComparer.Ordinal);

        return new ForgeName(Enumerable.Range(1, taken.Count + 1).Select(number => number == 1 ? stem : $"{stem}-{number}").First(name => !taken.Contains(name)));
    }

    private static string Label(string host)
    {
        var labels = host.Split('.');
        var label = labels is [var first, _, ..] && first is "www" or "api" or "git" ? labels[1] : labels[0];
        var readable = new string([.. label.ToLowerInvariant().Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')]);

        return readable.Length > 0 ? readable : "forge";
    }
}
