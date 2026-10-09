using Avala.Sdk;

namespace Avala.Workbench.Linking;

internal enum LinkRefusal
{
    NotAWebLink,
    Unavailable,
    Refused,
}

internal sealed class Links(ILinkOpener opener)
{
    public static Option<Uri> Web(string text) =>
        Uri.TryCreate(text.Trim(), UriKind.Absolute, out var link)
        && (link.Scheme == Uri.UriSchemeHttps || link.Scheme == Uri.UriSchemeHttp)
        && link.Host.Length > 0
        && link.UserInfo.Length == 0
            ? link
            : Option<Uri>.None;

    public Task<Result<Uri, LinkRefusal>> OpenAsync(string text, CancellationToken cancellationToken) =>
        Web(text).Match(
            link => OpenWebAsync(link, cancellationToken),
            () => Task.FromResult<Result<Uri, LinkRefusal>>(LinkRefusal.NotAWebLink));

    private async Task<Result<Uri, LinkRefusal>> OpenWebAsync(Uri link, CancellationToken cancellationToken) =>
        (await opener.OpenAsync(link, cancellationToken)).Match<Result<Uri, LinkRefusal>>(
            opened => opened,
            error => error == FileOpenError.Unavailable ? LinkRefusal.Unavailable : LinkRefusal.Refused);
}
