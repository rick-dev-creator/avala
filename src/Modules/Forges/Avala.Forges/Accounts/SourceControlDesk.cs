using Avala.Forges.Connecting;
using Avala.Forges.Contracts;
using Avala.Forges.Policy;
using Avala.Sdk;

namespace Avala.Forges.Accounts;

internal sealed record DetectedAccount(string Forge, Option<Uri> Url, string Host, CredentialSource Source, Option<string> Reference, Option<string> Login);

internal sealed record DetectedAccounts(IReadOnlyList<DetectedAccount> Accounts, IReadOnlyList<SignInTool> Tools);

internal interface ILoginDetection
{
    ValueTask<DetectedAccounts> DetectAsync(CancellationToken cancellationToken);
}

internal sealed class SourceControlDesk(ForgeConnections connections, IEnumerable<IForge> forges, ILoginDetection detection) : ISourceControlAccounts
{
    public async ValueTask<SourceControlAccounts> AccountsAsync(CancellationToken cancellationToken)
    {
        var catalog = await connections.CatalogAsync(cancellationToken);
        var declared = catalog.Connections.Select(Declared).ToList();
        var detected = await detection.DetectAsync(cancellationToken);
        var taken = declared.Select(account => account.Name.Value).ToHashSet(StringComparer.Ordinal);
        var suggested = new List<AccountStatus>();

        foreach (var account in detected.Accounts.Where(found => !declared.Exists(known => Same(known, found))))
        {
            var name = AccountNames.Suggest(account.Host.Length > 0 ? account.Host : account.Forge, taken);
            taken.Add(name.Value);
            suggested.Add(new AccountStatus(name, account.Forge, account.Host, account.Source, account.Login.Map(login => new ForgeAccount(login)), Option<ForgeError>.None, AccountOrigin.Detected)
            {
                Url = account.Url,
                Reference = account.Reference,
            });
        }

        return new SourceControlAccounts([.. declared, .. suggested], detected.Tools);
    }

    private static bool Same(AccountStatus declared, DetectedAccount detected) =>
        declared.ForgeKind == detected.Forge
        && declared.Source == detected.Source
        && declared.Reference == detected.Reference
        && string.Equals(declared.Host, detected.Host, StringComparison.OrdinalIgnoreCase);

    private AccountStatus Declared(ForgeConnectionInfo connection)
    {
        var url = connection.Url.Match(Option<Uri>.Some, () => Kind(connection.Forge).Bind(forge => forge.Info.DefaultUrl));

        return new AccountStatus(
            connection.Name,
            connection.Forge,
            url.Match(found => HostOf(connection.Forge, found), () => string.Empty),
            connection.Source,
            Option<ForgeAccount>.None,
            connection.Problem,
            AccountOrigin.Declared)
        {
            Url = url,
            Reference = connection.Reference,
        };
    }

    private Option<IForge> Kind(string forge) => forges.FirstOrDefault(found => found.Info.Id == forge).ToOption();

    private string HostOf(string forge, Uri url) =>
        Kind(forge).Match(found => found is IForgeRepositories hosting ? hosting.GitHost(url) : url.Host, () => url.Host);
}

internal static class AccountNames
{
    public static ForgeName Suggest(string host, IReadOnlySet<string> taken)
    {
        var label = host.Split('.') is [var first, _, ..] && first is "www" or "api" or "git" ? host.Split('.')[1] : host.Split('.')[0];
        var readable = new string([.. label.ToLowerInvariant().Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '-')]).Trim('-');
        var stem = ForgeNames.IsValid(readable) ? readable[..Math.Min(readable.Length, ForgeNames.Longest - 4)] : "forge";
        var candidate = Enumerable.Range(1, taken.Count + 1).Select(number => number == 1 ? stem : $"{stem}-{number}").First(name => !taken.Contains(name));

        return new ForgeName(candidate);
    }
}
