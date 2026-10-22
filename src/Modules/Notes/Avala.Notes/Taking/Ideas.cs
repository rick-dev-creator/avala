using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Notes.Contracts;
using Avala.Sdk;

namespace Avala.Notes.Taking;

internal sealed class Ideas(INotes notes, IJobs jobs, IIssuePublishing issues) : IIdeas
{
    public async ValueTask<Result<JobId, IdeaRejection>> StartJobAsync(NoteId idea, CancellationToken cancellationToken)
    {
        if (!(await IdeaAsync(idea, cancellationToken)).TryGetValue(out var note, out var refusal))
        {
            return refusal;
        }

        var request = new JobRequest(note.Place.Repository, IdeaBrief.Instruction(note)) { Attachments = IdeaBrief.Attachments(note) };

        return (await jobs.SubmitAsync(request, cancellationToken)).MapError(_ => IdeaRejection.JobRefused);
    }

    public async ValueTask<Result<Uri, IdeaRejection>> PublishIssueAsync(NoteId idea, CancellationToken cancellationToken)
    {
        if (!(await IdeaAsync(idea, cancellationToken)).TryGetValue(out var note, out var refusal))
        {
            return refusal;
        }

        return (await issues.PublishAsync(note.Place.Repository, IdeaBrief.Issue(note), cancellationToken)).Map(issue => issue.Link).MapError(_ => IdeaRejection.IssueRefused);
    }

    private async ValueTask<Result<NoteRecord, IdeaRejection>> IdeaAsync(NoteId idea, CancellationToken cancellationToken)
    {
        var found = (await notes.OpenAsync(idea, cancellationToken)).ToResult(IdeaRejection.UnknownNote);

        return found.TryGetValue(out var note, out var refusal)
            ? note.State == NoteState.Idea ? note : IdeaRejection.NotAnIdea
            : refusal;
    }
}
