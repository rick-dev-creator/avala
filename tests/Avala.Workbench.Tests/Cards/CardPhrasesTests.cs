using Avala.Agents.Contracts.Sessions;
using Avala.Permissions.Contracts;
using Avala.Workbench.Cards;

namespace Avala.Workbench.Tests.Cards;

public sealed class CardPhrasesTests
{
    [Theory]
    [InlineData(AgentError.SessionClosed, "The agent's session has closed.")]
    [InlineData(AgentError.NoPendingForm, "The agent no longer waits for this form.")]
    [InlineData(AgentError.InvalidAnswer, "The answer does not fit the form.")]
    [InlineData(AgentError.Unsupported, "This agent does not take answers to forms.")]
    [InlineData(AgentError.ProviderUnavailable, "The answer did not reach the agent.")]
    public void AnAnswerTheAgentRefusedSaysWhy(AgentError error, string phrase) =>
        Assert.Equal(phrase, CardPhrases.Error(error));

    [Theory]
    [InlineData(PolicyError.NotAwaitingAnswer, "The agent no longer waits for this answer.")]
    [InlineData(PolicyError.Unreadable, "The answer could not be recorded.")]
    public void AnAnswerThePolicyRefusedSaysWhy(PolicyError error, string phrase) =>
        Assert.Equal(phrase, CardPhrases.Error(error));
}
