using System.Globalization;
using System.Text.RegularExpressions;
using Avala.Forges.Connecting;
using Avala.Forges.Contracts;
using Avala.Forges.Delivering;
using Avala.Forges.Policy;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Forges.Watching;

internal sealed partial class MentionedPullRequests(JobPlaces places, IGitRemote git, ForgeConnections connections, IEnumerable<IForge> forges) : IMentionedPullRequests
{
    public async ValueTask<Option<MentionedPullRequest>> UnfollowedAsync(JobId job, string text, CancellationToken cancellationToken) =>
        await (await places.WorktreeAsync(job, cancellationToken)).Match(
            worktree => InAsync(worktree.Path, text, cancellationToken),
            () => Task.FromResult(Option<MentionedPullRequest>.None));

    private async Task<Option<MentionedPullRequest>> InAsync(string worktree, string text, CancellationToken cancellationToken)
    {
        if (!(await git.UrlAsync(worktree, PullRequestRules.DefaultRemote, cancellationToken)).TryGetValue(out var remote, out _)
            || (await connections.HostingAsync(remote, cancellationToken)).IsSome)
        {
            return Option<MentionedPullRequest>.None;
        }

        return RemoteAddresses.Parse(remote).Bind(address => Mentioned(text, address));
    }

    private Option<MentionedPullRequest> Mentioned(string text, RepositoryAddress origin)
    {
        var segments = forges.Select(forge => forge.Info.PullRequestSegment).ToHashSet(StringComparer.Ordinal);

        foreach (Match found in Link().Matches(text))
        {
            var parts = Uri.TryCreate(found.Value.TrimEnd('.', ',', ';', ':', '!', '?'), UriKind.Absolute, out var link) ? link.AbsolutePath.Trim('/').Split('/') : [];

            if (parts.Length >= 4
                && string.Equals(parts[^4], origin.Owner, StringComparison.OrdinalIgnoreCase)
                && string.Equals(parts[^3], origin.Name, StringComparison.OrdinalIgnoreCase)
                && segments.Contains(parts[^2])
                && int.TryParse(parts[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                return new MentionedPullRequest(number, link!);
            }
        }

        return Option<MentionedPullRequest>.None;
    }

    [GeneratedRegex(@"https?://[^\s<>""')\]]+")]
    private static partial Regex Link();
}
