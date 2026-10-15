using Avala.Sdk;
using Avala.Sdk.Presentation;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Linking;

internal enum LinkRefusal
{
    NotAWebLink,
    Unavailable,
    Refused,
}

internal sealed class Links(ILinkOpener opener, IMessenger messenger)
{
    private static readonly string[] LoopbackHosts = ["localhost", "127.0.0.1", "[::1]"];

    public static Option<Uri> Web(string text) =>
        Uri.TryCreate(text.Trim(), UriKind.Absolute, out var link)
        && (link.Scheme == Uri.UriSchemeHttps || link.Scheme == Uri.UriSchemeHttp)
        && link.Host.Length > 0
        && link.UserInfo.Length == 0
            ? link
            : Option<Uri>.None;

    public Task<Result<Uri, LinkRefusal>> OpenAsync(string text, CancellationToken cancellationToken) =>
        Web(text).Match(
            link => ShownHere(link) ? Task.FromResult<Result<Uri, LinkRefusal>>(link) : OpenWebAsync(link, cancellationToken),
            () => Task.FromResult<Result<Uri, LinkRefusal>>(LinkRefusal.NotAWebLink));

    public Task<Result<Uri, LinkRefusal>> OpenFileAsync(string path, CancellationToken cancellationToken) =>
        Path.IsPathFullyQualified(path)
            ? OpenWebAsync(new Uri(path), cancellationToken)
            : Task.FromResult<Result<Uri, LinkRefusal>>(LinkRefusal.Refused);

    private bool ShownHere(Uri link) =>
        LoopbackHosts.Contains(link.Host, StringComparer.OrdinalIgnoreCase) && messenger.Send(new LocalPageRequested(link)).IsShown;

    private async Task<Result<Uri, LinkRefusal>> OpenWebAsync(Uri link, CancellationToken cancellationToken) =>
        (await opener.OpenAsync(link, cancellationToken)).Match<Result<Uri, LinkRefusal>>(
            opened => opened,
            error => error == FileOpenError.Unavailable ? LinkRefusal.Unavailable : LinkRefusal.Refused);
}

internal sealed record LinkTools(IClipboard Clipboard, Links Links);
