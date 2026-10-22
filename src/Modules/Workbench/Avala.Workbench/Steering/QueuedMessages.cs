using System.Collections.Immutable;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Workbench.Contracts.Presentation;
using Avala.Workbench.Drafting;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Steering;

internal sealed record UndeliveredMessage(JobStatus Status, Option<JobRejection> Rejection);

internal sealed class QueuedMessages(IJobMessages jobs, DraftBook drafts, IMessenger messenger) : IHandle<JobProgressed>, IAsyncDisposable
{
    private readonly SerialExecutor owner = new();
    private ImmutableDictionary<JobId, ImmutableList<JobMessage>> queued = ImmutableDictionary<JobId, ImmutableList<JobMessage>>.Empty;
    private ImmutableDictionary<JobId, UndeliveredMessage> undelivered = ImmutableDictionary<JobId, UndeliveredMessage>.Empty;
    private ImmutableDictionary<JobId, JobStatus> ended = ImmutableDictionary<JobId, JobStatus>.Empty;

    public Option<string> Find(JobId job) =>
        Volatile.Read(ref queued).TryGetValue(job, out var messages) && Persons(messages).ToList() is { Count: > 0 } persons
            ? string.Join("\n\n", persons.Select(message => message.Text))
            : Option<string>.None;

    public bool Withdraw(JobId job)
    {
        var withdrawn = ImmutableList<JobMessage>.Empty;
        ImmutableInterlocked.Update(ref queued, known =>
        {
            withdrawn = known.TryGetValue(job, out var messages) ? [.. Persons(messages)] : [];
            var left = withdrawn.IsEmpty ? ImmutableList<JobMessage>.Empty : messages!.RemoveAll(message => message.Author == MessageAuthor.Person);

            return withdrawn.IsEmpty ? known : left.IsEmpty ? known.Remove(job) : known.SetItem(job, left);
        });
        drafts.Unwait(job, withdrawn);
        Tell(job, withdrawn, QueueChange.Withdrawn);

        return !withdrawn.IsEmpty;
    }

    public Option<UndeliveredMessage> TakeBack(JobId job) =>
        ImmutableInterlocked.TryRemove(ref undelivered, job, out var message) ? message : Option<UndeliveredMessage>.None;

    public Task<Result<MessageDelivery, JobRejection>> DeliverAsync(JobId job, JobStatus shown, JobMessage message, CancellationToken cancellationToken) =>
        owner.RunAsync(token => DeliveredAsync(job, Volatile.Read(ref ended).GetValueOrDefault(job, shown), message, token), cancellationToken);

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

        Take(job, messages);
        var (first, rest) = Split(messages);
        var sent = await KeptAsync(job, messages, () => jobs.SendBackAsync(job, first, cancellationToken));
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
            ImmutableInterlocked.AddOrUpdate(ref ended, job, integrationEvent.Status, (_, _) => integrationEvent.Status);

            if (ImmutableInterlocked.TryRemove(ref queued, job, out var left))
            {
                Undeliver(job, left, integrationEvent.Status, Option<JobRejection>.None);
            }

            return;
        }

        if (integrationEvent.Status is not (JobStatus.NeedsHelp or JobStatus.Approved) || !ImmutableInterlocked.TryRemove(ref queued, job, out var messages))
        {
            return;
        }

        Take(job, messages);
        var (first, rest) = Split(messages);
        var continued = await KeptAsync(job, messages, () => jobs.ContinueAsync(job, first, cancellationToken));

        if (continued.IsSuccess)
        {
            await JoinAsync(job, rest, cancellationToken);
        }
        else if (!continued.TryGetValue(out _, out var rejection) && Final(integrationEvent.Status, rejection))
        {
            Undeliver(job, messages, integrationEvent.Status, rejection);
        }
        else
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
            if (!(await KeptAsync(job, [note], () => jobs.SteerAsync(job, note, cancellationToken))).IsSuccess)
            {
                Queue(job, note);
            }
        }
    }

    private void Undeliver(JobId job, ImmutableList<JobMessage> messages, JobStatus status, Option<JobRejection> rejection)
    {
        var persons = Persons(messages).ToList();
        Tell(job, messages.RemoveAll(message => message.Author == MessageAuthor.Person), QueueChange.Dropped);

        if (persons.Count == 0)
        {
            return;
        }

        ImmutableInterlocked.AddOrUpdate(ref undelivered, job, new UndeliveredMessage(status, rejection), (_, _) => new UndeliveredMessage(status, rejection));
        drafts.Return(job, persons);
        Tell(job, persons, QueueChange.Returned);
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

    private void Queue(JobId job, JobMessage message)
    {
        ImmutableInterlocked.AddOrUpdate(ref queued, job, [message], (_, earlier) => earlier.Add(message));
        drafts.Wait(job, [.. Persons([message])]);
        Tell(job, [message], QueueChange.Queued);
    }

    private void Take(JobId job, ImmutableList<JobMessage> messages)
    {
        drafts.Unwait(job, Persons(messages));
        Tell(job, messages, QueueChange.Taken);
    }

    private async Task<Result<T, JobRejection>> KeptAsync<T>(JobId job, IReadOnlyList<JobMessage> messages, Func<ValueTask<Result<T, JobRejection>>> send)
        where T : notnull
    {
        try
        {
            return await send();
        }
        catch
        {
            Requeue(job, [.. messages]);
            throw;
        }
    }

    private void Tell(JobId job, IReadOnlyList<JobMessage> messages, QueueChange change)
    {
        if (messages.Count > 0)
        {
            messenger.Send(new QueuedMessageChanged(job, [.. messages.SelectMany(message => message.Messages)], change));
        }
    }
}
