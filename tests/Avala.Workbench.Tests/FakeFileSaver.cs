using Avala.Sdk;

namespace Avala.Workbench.Tests;

internal sealed class FakeFileSaver : IFileSaver
{
    public List<(string Name, string Content)> Saved { get; } = [];

    public FileSaveError? Refusal { get; init; }

    public ValueTask<Result<string, FileSaveError>> SaveAsync(string title, string suggestedName, string content, CancellationToken cancellationToken)
    {
        if (Refusal is { } refusal)
        {
            return ValueTask.FromResult(Result<string, FileSaveError>.Failure(refusal));
        }

        Saved.Add((suggestedName, content));

        return ValueTask.FromResult<Result<string, FileSaveError>>($"/home/ana/{suggestedName}");
    }
}
