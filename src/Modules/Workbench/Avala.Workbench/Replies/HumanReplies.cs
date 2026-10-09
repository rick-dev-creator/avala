using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Replies;

internal sealed class HumanReplies(IPermissionAnswers permissions, IAgents agents)
{
    public async Task<Result<HumanAnswer, PolicyError>> AnswerAsync(
        PermissionEntry request,
        PermissionAnswer answer,
        Option<string> message,
        bool dontAskAgain,
        CancellationToken cancellationToken) =>
        await permissions.AnswerAsync(
            request.Session,
            new PermissionReply(request.Item, answer) { Message = message, DontAskAgain = dontAskAgain },
            cancellationToken);

    public async Task<Result<ItemId, AgentError>> AnswerAsync(FormEntry form, FormAnswer answer, CancellationToken cancellationToken) =>
        await agents.AnswerAsync(form.Session, answer, cancellationToken);
}
