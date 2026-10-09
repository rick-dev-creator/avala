namespace Avala.Agents.Contracts.Sessions;

public interface IAgentProviderDecorator
{
    IAgentProvider Decorate(IAgentProvider provider);
}
