using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Permissions.Contracts;

public sealed record JobTerms(bool ReadOnly, Option<JobId> AsksParent)
{
    public static JobTerms Full { get; } = new(ReadOnly: false, Option<JobId>.None);
}

public interface IJobTerms
{
    ValueTask<JobTerms> OfAsync(JobId job, CancellationToken cancellationToken);
}

public sealed record ParentReply(ItemId Item, SessionId Parent, PermissionAnswer Answer)
{
    public Option<string> Message { get; init; }
}

public sealed record ParentFormReply(SessionId Parent, FormAnswer Answer);

public interface IParentAnswers
{
    ValueTask<Result<HumanAnswer, PolicyError>> AnswerAsync(SessionId child, ParentReply reply, CancellationToken cancellationToken);

    ValueTask<Result<FormDecision, PolicyError>> AnswerFormAsync(SessionId child, ParentFormReply reply, CancellationToken cancellationToken);

    ValueTask<bool> PassAsync(SessionId child, ItemId item, PassReason reason, CancellationToken cancellationToken);

    bool Waits(SessionId child, ItemId item);
}
