using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Permissions.Tests;

internal sealed class AnsweringAgents : IAgents
{
    private readonly List<(SessionId, PermissionDecision)> responses = [];
    private readonly List<(SessionId, FormAnswer)> answers = [];

    public IReadOnlyList<(SessionId, PermissionDecision)> Responses => responses;

    public IReadOnlyList<(SessionId, FormAnswer)> Answers => answers;

    public Option<AgentError> Rejection { get; set; }

    public Action OnRespond { get; set; } = () => { };

    public ValueTask<Result<ItemId, AgentError>> RespondAsync(SessionId session, PermissionDecision decision, CancellationToken cancellationToken)
    {
        OnRespond();
        responses.Add((session, decision));

        return ValueTask.FromResult(Reply(decision.Item));
    }

    public ValueTask<Result<ItemId, AgentError>> AnswerAsync(SessionId session, FormAnswer answer, CancellationToken cancellationToken)
    {
        answers.Add((session, answer));

        return ValueTask.FromResult(Reply(answer.Item));
    }

    public ValueTask<Result<OpenedSession, AgentError>> OpenAsync(AgentRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<OpenedSession, AgentError>.Failure(AgentError.Unsupported));

    public bool IsOpen(SessionId session) => true;

    public ValueTask<Result<AgentTurn, AgentError>> SendAsync(SessionId session, string message, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<AgentTurn, AgentError>.Failure(AgentError.Unsupported));

    public ValueTask<Result<ItemId, AgentError>> ReturnAsync(SessionId session, ToolResult result, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<ItemId, AgentError>.Failure(AgentError.Unsupported));

    public ValueTask<Result<TurnId, AgentError>> InterruptAsync(SessionId session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<TurnId, AgentError>.Failure(AgentError.Unsupported));

    public ValueTask<Result<AgentTurn, AgentError>> SteerAsync(SessionId session, string message, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<AgentTurn, AgentError>.Failure(AgentError.Unsupported));

    public ValueTask<Result<SessionId, AgentError>> StopAsync(SessionId session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<SessionId, AgentError>.Failure(AgentError.Unsupported));

    private Result<ItemId, AgentError> Reply(ItemId item) =>
        Rejection.Match(error => Result<ItemId, AgentError>.Failure(error), () => Result<ItemId, AgentError>.Success(item));
}
