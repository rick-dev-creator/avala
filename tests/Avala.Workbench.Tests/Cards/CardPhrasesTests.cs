using Avala.Agents.Contracts.Events;
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

    [Theory]
    [InlineData(ItemKind.Command, "Wants to run a command")]
    [InlineData(ItemKind.FileEdit, "Wants to edit a file")]
    [InlineData(ItemKind.Web, "Wants to reach the web")]
    [InlineData(ItemKind.Mcp, "Wants to use a tool")]
    [InlineData(ItemKind.Search, "Asks permission")]
    public void APermissionCardSaysWhatTheAgentWants(ItemKind kind, string phrase) =>
        Assert.Equal(phrase, CardPhrases.Headline(kind));

    [Theory]
    [InlineData(FormPurpose.Question, "Asks a question")]
    [InlineData(FormPurpose.PlanApproval, "Asks you to approve a plan")]
    [InlineData(FormPurpose.Permission, "Asks permission")]
    [InlineData(FormPurpose.Other, "Asks for input")]
    public void AFormCardSaysWhatTheAgentAsks(FormPurpose purpose, string phrase) =>
        Assert.Equal(phrase, CardPhrases.Headline(purpose));
}
