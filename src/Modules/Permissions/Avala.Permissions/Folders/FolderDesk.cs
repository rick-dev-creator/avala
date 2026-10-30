using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Permissions.Folders;

internal sealed class FolderDesk(IAgents agents, FolderCoverage coverage, FolderAsking asking) : IHandle<AgentActivity>
{
    public async ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Event is not ToolCalled { Tool: FolderTool.Name } called)
        {
            return;
        }

        var governed = await coverage.GovernedAsync(called.Session, cancellationToken);
        var request = FolderTool.Read(called.Input, governed.WorkingDirectory).Map(coverage.Real);
        var answered = await request.Match(
            found => coverage.AnsweredAsync(governed, called, found, cancellationToken),
            () => Task.FromResult(Option<ToolResult>.Some(FolderTool.Malformed(called.Item))));

        await answered.Match(
            result => agents.ReturnAsync(called.Session, result, cancellationToken).AsTask(),
            () => request.Match(found => asking.AskAsync(governed, called, found, cancellationToken), () => Task.CompletedTask));
    }
}
