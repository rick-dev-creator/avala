using Avala.Jobs.Contracts;
using Avala.Notes.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Tests;

internal sealed class FakeIdeas : IIdeas
{
    public List<NoteId> Started { get; } = [];

    public List<NoteId> Published { get; } = [];

    public Option<IdeaRejection> Refusal { get; set; }

    public ValueTask<Result<JobId, IdeaRejection>> StartJobAsync(NoteId idea, CancellationToken cancellationToken)
    {
        Started.Add(idea);

        return ValueTask.FromResult(Refusal.Match(refused => Result<JobId, IdeaRejection>.Failure(refused), () => Result<JobId, IdeaRejection>.Success(JobId.New())));
    }

    public ValueTask<Result<Uri, IdeaRejection>> PublishIssueAsync(NoteId idea, CancellationToken cancellationToken)
    {
        Published.Add(idea);

        return ValueTask.FromResult(Refusal.Match(refused => Result<Uri, IdeaRejection>.Failure(refused), () => Result<Uri, IdeaRejection>.Success(new Uri("https://forge.example.com/acme/shop/issues/7"))));
    }
}
