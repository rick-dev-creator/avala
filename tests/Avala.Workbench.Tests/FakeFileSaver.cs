using Avala.Sdk;

namespace Avala.Workbench.Tests;

internal sealed class FakeFileSaver : IFileSaver
{
    private readonly TaskCompletionSource saved = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<(string Name, string Content)> Saved { get; } = [];

    public Task SavedAsync() => saved.Task;

    public FileSaveError? Refusal { get; init; }

    public ValueTask<Result<string, FileSaveError>> SaveAsync(string title, string suggestedName, string content, CancellationToken cancellationToken)
    {
        if (Refusal is { } refusal)
        {
            return ValueTask.FromResult(Result<string, FileSaveError>.Failure(refusal));
        }

        Saved.Add((suggestedName, content));
        saved.TrySetResult();

        return ValueTask.FromResult<Result<string, FileSaveError>>($"/home/ana/{suggestedName}");
    }
}
