using System.Collections.Immutable;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Workbench.Steering;

internal sealed class QueuedMessages(IJobMessages jobs) : IHandle<JobProgressed>, IAsyncDisposable
{
    private readonly SerialExecutor owner = new();
    private ImmutableDictionary<JobId, ImmutableList<JobMessage>> queued = ImmutableDictionary<JobId, ImmutableList<JobMessage>>.Empty;

    public Option<string> Find(JobId job) =>
        Volatile.Read(ref queued).TryGetValue(job, out var messages) && Persons(messages).ToList() is { Count: > 0 } persons
            ? string.Join("\n\n", persons.Select(message => message.Text))
            : Option<string>.None;

    public bool Withdraw(JobId job)
    {
        var withdrawn = false;
        ImmutableInterlocked.Update(ref queued, known =>
        {
            withdrawn = known.TryGetValue(job, out var messages) && Persons(messages).Any();
            var left = withdrawn ? messages!.RemoveAll(message => message.Author == MessageAuthor.Person) : ImmutableList<JobMessage>.Empty;

            return !withdrawn ? known : left.IsEmpty ? known.Remove(job) : known.SetItem(job, left);
        });

        return withdrawn;
    }

    public Task<Result<MessageDelivery, JobRejection>> DeliverAsync(JobId job, JobStatus shown, JobMessage message, CancellationToken cancellationToken) =>
        owner.RunAsync(token => DeliveredAsync(job, shown, message, token), cancellationToken);

    public Task<Result<JobContinuation, JobRejection>> SendBackAsync(JobId job, CancellationToken cancellationToken) =>
        owner.RunAsync(token => SentBackAsync(job, token), cancellationToken);

    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken) =>
        await owner.RunAsync(token => SettleAsync(integrationEvent, token), cancellationToken);

    public ValueTask DisposeAsync() => owner.DisposeAsync();

    private async Task<Result<MessageDelivery, JobRejection>> DeliveredAsync(JobId job, JobStatus shown, JobMessage message, CancellationToken cancellationToken)
    {
        Task<Result<MessageDelivery, JobRejection>> ContinuedAsync() =>
            AttemptAsync(jobs.ContinueAsync(job, message, cancellationToken), MessageDelivery.Sent, [JobRejection.NotHeld], () => Task.FromResult(Queued(job, message)));

        Task<Result<MessageDelivery, JobRejection>> SteeredAsync() =>
            AttemptAsync(jobs.SteerAsync(job, message, cancellationToken), MessageDelivery.JoinedTheTurn, [JobRejection.NotRunning, JobRejection.NotSteerable], ContinuedAsync);

        return shown switch
        {
            JobStatus.Discarded or JobStatus.Failed => JobRejection.NotHeld,
            JobStatus.Approved => await AttemptAsync(jobs.ContinueAsync(job, message, cancellationToken), MessageDelivery.Sent, [JobRejection.NotHeld], SteeredAsync),
            JobStatus.AwaitingReview => await AttemptAsync(jobs.SendBackAsync(job, message, cancellationToken), MessageDelivery.Sent, [JobRejection.NotAwaitingReview], ContinuedAsync),
            JobStatus.Running => await SteeredAsync(),
            _ => await ContinuedAsync(),
        };
    }

    private static async Task<Result<MessageDelivery, JobRejection>> AttemptAsync<T>(
        ValueTask<Result<T, JobRejection>> attempt,
        MessageDelivery delivery,
        JobRejection[] elsewhere,
        Func<Task<Result<MessageDelivery, JobRejection>>> otherwise)
        where T : notnull =>
        await (await attempt).Match(
            _ => Task.FromResult(Result<MessageDelivery, JobRejection>.Success(delivery)),
            rejection => elsewhere.Contains(rejection) ? otherwise() : Task.FromResult(Result<MessageDelivery, JobRejection>.Failure(rejection)));

    private Result<MessageDelivery, JobRejection> Queued(JobId job, JobMessage message)
    {
        Queue(job, message);

        return MessageDelivery.Queued;
    }

    private async Task<Result<JobContinuation, JobRejection>> SentBackAsync(JobId job, CancellationToken cancellationToken)
    {
        if (!ImmutableInterlocked.TryRemove(ref queued, job, out var messages))
        {
            return JobRejection.EmptyMessage;
        }

        var (first, rest) = Split(messages);
        var sent = await jobs.SendBackAsync(job, first, cancellationToken);
        if (sent.IsSuccess)
        {
            await JoinAsync(job, rest, cancellationToken);
        }
        else
        {
            Requeue(job, messages);
        }

        return sent;
    }

    private async Task SettleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        var job = integrationEvent.Job;

        if (integrationEvent.Status is JobStatus.Discarded or JobStatus.Failed)
        {
            ImmutableInterlocked.TryRemove(ref queued, job, out _);
            return;
        }

        if (integrationEvent.Status is not (JobStatus.NeedsHelp or JobStatus.Approved) || !ImmutableInterlocked.TryRemove(ref queued, job, out var messages))
        {
            return;
        }

        var (first, rest) = Split(messages);
        var continued = await jobs.ContinueAsync(job, first, cancellationToken);

        if (continued.IsSuccess)
        {
            await JoinAsync(job, rest, cancellationToken);
        }
        else if (continued.Match(_ => false, rejection => !Final(integrationEvent.Status, rejection)))
        {
            Requeue(job, messages);
        }
    }

    private static IEnumerable<JobMessage> Persons(ImmutableList<JobMessage> messages) => messages.Where(message => message.Author == MessageAuthor.Person);

    private static (JobMessage First, Option<JobMessage> Notes) Split(ImmutableList<JobMessage> messages)
    {
        var persons = messages.Where(message => message.Author == MessageAuthor.Person).ToList();
        var notes = messages.Where(message => message.Author == MessageAuthor.Avala).ToList();

        return persons.Count == 0
            ? (Merged(notes), Option<JobMessage>.None)
            : (Merged(persons), notes.Count == 0 ? Option<JobMessage>.None : Merged(notes));
    }

    private static JobMessage Merged(List<JobMessage> messages) =>
        new(string.Join("\n\n", messages.Select(message => message.Text)))
        {
            Messages = [.. messages.SelectMany(message => message.Messages)],
            Attachments = [.. messages.SelectMany(message => message.Attachments)],
            Author = messages[0].Author,
            CausedBy = messages.Any(message => message.CausedBy == MessageAuthor.Person) ? MessageAuthor.Person : MessageAuthor.Avala,
        };

    private async Task JoinAsync(JobId job, Option<JobMessage> rest, CancellationToken cancellationToken)
    {
        foreach (var note in rest.Match<JobMessage[]>(found => [found], () => []))
        {
            if (!(await jobs.SteerAsync(job, note, cancellationToken)).IsSuccess)
            {
                Queue(job, note);
            }
        }
    }

    private void Requeue(JobId job, ImmutableList<JobMessage> messages)
    {
        foreach (var message in messages)
        {
            Queue(job, message);
        }
    }

    private static bool Final(JobStatus status, JobRejection rejection) =>
        rejection == JobRejection.NotRoot || (status == JobStatus.Approved && rejection == JobRejection.WorkspaceUnavailable);

    private void Queue(JobId job, JobMessage message) =>
        ImmutableInterlocked.AddOrUpdate(ref queued, job, [message], (_, earlier) => earlier.Add(message));
}
