using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;

namespace Avala.Agents.Tests.Conformance;

internal static class MidTurnConformance
{
    public static async Task<IReadOnlyList<string>> CheckMessageWhileCallPendingAsync(
        IAgentProvider provider,
        SessionOptions options,
        HarnessTool tool,
        UserTurn instruction,
        CancellationToken deadline)
    {
        if (!AgentConformance.Accepts(provider, options, ToolSurface.Executed) || !provider.CapabilitiesOn(options.Connection).Has<AcceptsMessagesMidTurn>())
        {
            return await AgentConformance.CheckTurnAsync(provider, options with { Tools = [] }, instruction, deadline);
        }

        var replies = AgentConformance.Allowing with
        {
            Tool = async (session, called, token) =>
            {
                var sent = await session.SendAsync(new UserTurn("A sub-agent is waiting for you.") { MidTurn = true }, token);

                return
                [
                    .. sent.IsFailure ? [$"a message sent while the call {called.Item.Value} was pending was refused"] : Array.Empty<string>(),
                    .. await AgentConformance.ReturnAsync(session, called, token),
                ];
            },
            AfterTurn = (_, events, _) => Task.FromResult<IReadOnlyList<string>>(
                events.OfType<MessageQueued>().Any() ? [] : ["no message was queued into the turn while a call of the tool was pending"]),
        };

        return (await AgentConformance.RunAsync(provider, options with { Tools = [tool] }, instruction, replies, deadline)).Violations;
    }
}
