using Avala.Sdk;
using Avala.Workbench.Linking;

namespace Avala.Workbench.Tests;

internal sealed class FakeLinks : ILinkOpener
{
    public List<Uri> Opened { get; } = [];

    public FileOpenError? Refusal { get; init; }

    public static Links Opening => new(new FakeLinks());

    public ValueTask<Result<Uri, FileOpenError>> OpenAsync(Uri link, CancellationToken cancellationToken)
    {
        if (Refusal is { } refusal)
        {
            return ValueTask.FromResult(Result<Uri, FileOpenError>.Failure(refusal));
        }

        Opened.Add(link);

        return ValueTask.FromResult<Result<Uri, FileOpenError>>(link);
    }
}
