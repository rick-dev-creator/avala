using System.Globalization;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk.Events;
using Avala.Verification.Checks;
using Avala.Verification.Contracts;

namespace Avala.Verification.Discovering;

internal sealed class ProposalDesk(DiscoveryBook book, IAgents agents, IPermissionAnswers answers)
    : IHandle<JobSessionStarted>, IHandle<AgentActivity>, IHandle<PermissionDecided>, IHandle<FormDecided>
{
    public const string Unasked = "Nobody answers Avala's checks scout. Decide on your own, then propose the checks.";

    public const string Refusal = "Avala's checks scout runs nothing that would wait for a person. Read the files instead, then propose the checks.";

    private readonly Dictionary<SessionId, JobId> sessions = [];

    public ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        sessions[integrationEvent.Session] = integrationEvent.Job;

        return ValueTask.CompletedTask;
    }

    public async ValueTask HandleAsync(PermissionDecided integrationEvent, CancellationToken cancellationToken)
    {
        var decision = integrationEvent.Decision;
        var scouting = decision.Job.Bind(book.OfScout);

        await scouting.Match(
            async discovery =>
            {
                if (decision.Delivery == DecisionDelivery.LeftToHuman)
                {
                    _ = await answers.AnswerAsync(decision.Session, new PermissionReply(decision.Item, PermissionAnswer.Deny) { Message = Refusal }, cancellationToken);
                }
                else if (decision.Answer == PolicyAnswer.Allow && decision.Kind == ItemKind.Command)
                {
                    await book.KeepAsync(discovery with { Ran = ProposalReview.Ran(discovery.Ran, decision.Target) }, cancellationToken);
                }
            },
            () => Task.CompletedTask);
    }

    public async ValueTask HandleAsync(FormDecided integrationEvent, CancellationToken cancellationToken)
    {
        var decision = integrationEvent.Decision;

        if (decision.Delivery == DecisionDelivery.LeftToHuman && decision.Job.Match(book.IsScout, () => false))
        {
            _ = await agents.AnswerAsync(decision.Session, new FormAnswer(decision.Item, []) { Declined = true, Message = Unasked, AnsweredBy = MessageAuthor.Avala }, cancellationToken);
        }
    }

    public async ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Event is not ToolCalled { Tool: ChecksProposal.Tool } called)
        {
            return;
        }

        var discovery = sessions.TryGetValue(called.Session, out var scout) ? book.OfScout(scout) : default;
        var result = await discovery.Match(
            found => CheckDeclaration.Parse(called.Input).Match(
                async checks =>
                {
                    await book.KeepAsync(found with { Proposal = CheckDeclaration.Write(checks) }, cancellationToken);

                    return new ToolResult(called.Item, string.Create(CultureInfo.InvariantCulture, $"Avala received {checks.Count} {(checks.Count == 1 ? "check" : "checks")}. Finish your turn now."));
                },
                _ => Task.FromResult(new ToolResult(called.Item, "The checks are not a JSON object with a checks array of objects that each name a command. Call the tool again with valid checks.") { IsError = true })),
            () => Task.FromResult(new ToolResult(called.Item, "Only Avala's checks scout proposes checks.") { IsError = true }));

        _ = await agents.ReturnAsync(called.Session, result, cancellationToken);
    }
}
