using Avala.Forges.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Forges.Accounts;

internal sealed class CloneUrls(IRemoteRepositories remotes, AvalaPaths paths)
{
    public async Task<IReadOnlyList<RemoteRepository>> ChosenAsync(IReadOnlyList<ListedRepository> listed, CancellationToken cancellationToken)
    {
        var helpers = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var chosen = new List<RemoteRepository>();

        foreach (var found in listed)
        {
            var hosted = found.Repository;
            var https = hosted.SshUrl.Length == 0 || await HelperAsync(hosted.HttpsUrl, helpers, cancellationToken);
            chosen.Add(new RemoteRepository(hosted.Address, hosted.Link, found.Private, found.Archived, found.UpdatedAt, https ? hosted.HttpsUrl : hosted.SshUrl));
        }

        return chosen;
    }

    private async Task<bool> HelperAsync(string url, Dictionary<string, bool> helpers, CancellationToken cancellationToken)
    {
        var site = Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? $"{parsed.Scheme}://{parsed.Authority}/" : url;

        if (!helpers.TryGetValue(site, out var helped))
        {
            helped = await remotes.HasCredentialHelperAsync(paths.Data, site, cancellationToken);
            helpers[site] = helped;
        }

        return helped;
    }
}
