using Avala.Forges.Contracts;
using Avala.Sdk;
using Avala.Workbench.Machine;

namespace Avala.Workbench.Tests.Accounts;

internal static class Kinds
{
    public static ForgeInfo Hub { get; } = new("hub", "Hub") { DefaultUrl = new Uri("https://api.hub.example"), Logo = "M0 0H16V16H0Z" };

    public static ForgeInfo Cup { get; } = new("cup", "Cup") { Logo = "M2 2H14V14H2Z" };

    public static ForgeInfo Saucer { get; } = new("saucer", "Saucer") { SuggestedUrl = new Uri("https://saucer.example"), Logo = "M4 4H12V12H4Z" };

    public static SignInTool HubCli { get; } = new("hub", ["hub"], "hub auth login", Installed: true);

    public static SignInTool CupCli { get; } = new("cup", ["cup", "saucer"], "cup login add", Installed: true);

    public static AccountStatus Declared(string name, ForgeInfo kind, CredentialSource source, string? login = null, ForgeError? problem = null, string? reference = null) =>
        new(new ForgeName(name), kind.Id, kind.DefaultUrl.Match(url => url.Host, () => $"{name}.example"), source, login.ToOption().Map(found => new ForgeAccount(found)), problem.ToOption(), AccountOrigin.Declared)
        {
            Url = kind.DefaultUrl,
            Reference = reference.ToOption(),
        };

    public static AccountStatus Detected(string name, ForgeInfo kind, CredentialSource source, string? login, string host, Uri? url = null, string? reference = null) =>
        new(new ForgeName(name), kind.Id, host, source, login.ToOption().Map(found => new ForgeAccount(found)), Option<ForgeError>.None, AccountOrigin.Detected)
        {
            Url = url is not null ? url : kind.DefaultUrl.IsSome ? kind.DefaultUrl : host.Length > 0 ? new Uri($"https://{host}") : Option<Uri>.None,
            Reference = reference.ToOption(),
        };
}

internal sealed class FakeSourceControl : ISourceControlAccounts, IForgeConnections, IForgeCatalog
{
    public List<AccountStatus> Accounts { get; } = [];

    public List<SignInTool> Tools { get; } = [];

    public List<ForgeInfo> Installed { get; } = [Kinds.Hub, Kinds.Cup, Kinds.Saucer];

    public List<ForgeConnectionEdit> Connected { get; } = [];

    public List<ForgeName> Removed { get; } = [];

    public Option<ForgeError> Refusal { get; set; }

    public Dictionary<string, Result<IReadOnlyList<RemoteRepository>, ForgeError>> Repositories { get; } = new(StringComparer.Ordinal);

    public List<(string Forge, string Search)> Listed { get; } = [];

    public int Reads { get; private set; }

    public SourceControl Source => new(this, this, this);

    public ValueTask<SourceControlAccounts> AccountsAsync(CancellationToken cancellationToken)
    {
        Reads++;

        return ValueTask.FromResult(new SourceControlAccounts([.. Accounts], [.. Tools]));
    }

    public ValueTask<Result<IReadOnlyList<RemoteRepository>, ForgeError>> RepositoriesAsync(ForgeName forge, string search, CancellationToken cancellationToken)
    {
        Listed.Add((forge.Value, search));

        return ValueTask.FromResult(Repositories.TryGetValue(forge.Value, out var found) ? found : ForgeError.UnknownConnection);
    }

    public ValueTask<Result<ForgeCatalog, ForgeError>> ConnectAsync(ForgeConnectionEdit connection, CancellationToken cancellationToken)
    {
        if (Refusal.IsSome)
        {
            return ValueTask.FromResult(Result<ForgeCatalog, ForgeError>.Failure(Refusal.Match(error => error, () => ForgeError.Rejected)));
        }

        Connected.Add(connection);
        Accounts.RemoveAll(account => account.Name == connection.Name);
        var kind = Installed.First(found => found.Id == connection.Forge);
        Accounts.Add(new AccountStatus(connection.Name, connection.Forge, connection.Url.Match(url => url.Host, () => kind.DefaultUrl.Match(url => url.Host, () => string.Empty)), connection.Source, Option<ForgeAccount>.None, Option<ForgeError>.None, AccountOrigin.Declared)
        {
            Url = connection.Url,
            Reference = connection.Reference,
        });

        return ValueTask.FromResult(Result<ForgeCatalog, ForgeError>.Success(Listing()));
    }

    public ValueTask<Result<ForgeCatalog, ForgeError>> RemoveAsync(ForgeName name, CancellationToken cancellationToken)
    {
        Removed.Add(name);
        Accounts.RemoveAll(account => account.Name == name);

        return ValueTask.FromResult(Result<ForgeCatalog, ForgeError>.Success(Listing()));
    }

    public ValueTask<ForgeCatalog> CatalogAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Listing());

    private ForgeCatalog Listing() => new([.. Installed], [], Option<ForgeError>.None, TimeSpan.FromSeconds(60));
}
