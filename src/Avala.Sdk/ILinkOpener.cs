namespace Avala.Sdk;

public interface ILinkOpener
{
    ValueTask<Result<Uri, FileOpenError>> OpenAsync(Uri link, CancellationToken cancellationToken);
}
