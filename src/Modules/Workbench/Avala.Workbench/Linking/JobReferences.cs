using System.Globalization;
using System.Text.RegularExpressions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Linking;

internal enum ReferenceKind
{
    File,
    Commit,
    Issue,
}

internal enum ReferenceRefusal
{
    NoWebAddress,
    Unopened,
}

internal sealed record Reference(ReferenceKind Kind, string Target, int Line)
{
    private const string Prefix = "avala-ref:";

    public static Option<Reference> Parse(string link)
    {
        if (!link.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return Option<Reference>.None;
        }

        var rest = link[Prefix.Length..];
        var slash = rest.IndexOf('/', StringComparison.Ordinal);

        if (slash < 0)
        {
            return Option<Reference>.None;
        }

        var (kind, body) = (rest[..slash], rest[(slash + 1)..]);
        var hash = body.LastIndexOf("#L", StringComparison.Ordinal);
        var line = hash >= 0 && int.TryParse(body[(hash + 2)..], NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0;
        var target = Uri.UnescapeDataString(hash >= 0 ? body[..hash] : body);

        return kind switch
        {
            "file" when target.Length > 0 => new Reference(ReferenceKind.File, target, line),
            "commit" when target.Length > 0 => new Reference(ReferenceKind.Commit, target, 0),
            "issue" when target.Length > 0 => new Reference(ReferenceKind.Issue, target, 0),
            _ => Option<Reference>.None,
        };
    }
}

internal sealed partial class JobReferences(IJobCatalog catalog, IRemoteRepositories remotes, Links links)
{
    public static Option<Uri> Web(string remote)
    {
        var url = remote.Trim();

        if (Scp().Match(url) is { Success: true } scp)
        {
            url = $"https://{scp.Groups["host"].Value}/{scp.Groups["path"].Value}";
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Host.Length == 0 || parsed.Scheme is not ("https" or "http" or "ssh" or "git"))
        {
            return Option<Uri>.None;
        }

        var path = parsed.AbsolutePath.TrimEnd('/');
        path = path.EndsWith(".git", StringComparison.Ordinal) ? path[..^4] : path;

        return path.Count(character => character == '/') < 2 ? Option<Uri>.None : new Uri($"https://{parsed.Host}{path}");
    }

    public async Task<Result<Uri, ReferenceRefusal>> OpenAsync(JobId job, Reference reference, CancellationToken cancellationToken)
    {
        var history = await catalog.HistoryAsync(job, cancellationToken);
        var remote = await history.Match(
            async found => await remotes.RemoteAsync(found.Summary.Repository, "origin", cancellationToken),
            () => Task.FromResult(Option<string>.None));
        return await remote.Bind(Web).Match(
            async site => (await links.OpenAsync($"{site}/{(reference.Kind == ReferenceKind.Commit ? "commit" : "issues")}/{Uri.EscapeDataString(reference.Target)}", cancellationToken))
                .Match<Result<Uri, ReferenceRefusal>>(opened => opened, _ => ReferenceRefusal.Unopened),
            () => Task.FromResult<Result<Uri, ReferenceRefusal>>(ReferenceRefusal.NoWebAddress));
    }

    [GeneratedRegex(@"^[\w.-]+@(?<host>[\w.-]+):(?<path>[^/].*)$")]
    private static partial Regex Scp();
}
