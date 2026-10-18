using Avala.Forges.Contracts;
using Avala.Sdk;

namespace Avala.Forges.Readiness;

internal sealed class ForgeReadinessDesk(IRepositoryPublishing publishing, ISourceControlAccounts accounts, IEnumerable<IForge> forges) : IForgeReadiness
{
    public async ValueTask<ForgeReadiness> OfAsync(string repository, CancellationToken cancellationToken)
    {
        var remote = await publishing.RemoteOfAsync(repository, cancellationToken);

        return await remote.Bind(found => found.Address.Map(address => (Remote: found, Address: address))).Match(
            async found => await found.Remote.Forge.Match(
                connection => Task.FromResult(new ForgeReadiness(ForgeReadinessKind.Hosted, found.Address.Host) { Connection = connection }),
                () => UnconnectedAsync(found.Address.Host, cancellationToken)),
            () => Task.FromResult(new ForgeReadiness(remote.IsSome ? ForgeReadinessKind.Unknown : ForgeReadinessKind.NoRemote, string.Empty)));
    }

    private async Task<ForgeReadiness> UnconnectedAsync(string host, CancellationToken cancellationToken)
    {
        var known = await accounts.AccountsAsync(cancellationToken);
        var candidates = known.Accounts.Where(account => account.Origin == AccountOrigin.Detected && Same(account.Host, host)).ToList();

        if (candidates.Count > 0)
        {
            var kind = candidates[0].ForgeKind;

            return new ForgeReadiness(ForgeReadinessKind.Connectable, host)
            {
                Accounts = candidates,
                Forge = Info(kind),
                SignIn = known.Tools.FirstOrDefault(found => found.Forges.Contains(kind, StringComparer.Ordinal)).ToOption(),
            };
        }

        var tool = known.Tools.FirstOrDefault(found => found.Forges.Any(kind => Serves(kind, host, known.Accounts)));

        return tool is null
            ? new ForgeReadiness(ForgeReadinessKind.Unknown, host)
            : new ForgeReadiness(ForgeReadinessKind.SignInNeeded, host) { SignIn = tool, Forge = Info(tool.Forges.First(kind => Serves(kind, host, known.Accounts))) };
    }

    private bool Serves(string kind, string host, IReadOnlyList<AccountStatus> known) =>
        known.Any(account => account.ForgeKind == kind && Same(account.Host, host))
        || Info(kind).Bind(info => info.DefaultUrl).Match(url => Same(GitHost(kind, url), host), () => false);

    private Option<ForgeInfo> Info(string kind) => forges.FirstOrDefault(forge => forge.Info.Id == kind).ToOption().Map(forge => forge.Info);

    private string GitHost(string kind, Uri url) =>
        forges.FirstOrDefault(forge => forge.Info.Id == kind) is IForgeRepositories hosting ? hosting.GitHost(url) : url.Host;

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
