using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Workbench.ModelChoices;

namespace Avala.Workbench.Tests.ModelChoices;

public sealed class ModelPhrasesTests
{
    [Fact]
    public void WhatASessionRanWithBreaksOnlyBetweenItsModelAndItsEffort()
    {
        var phrase = ModelPhrases.Ran(new ModelReported(SessionId.New(), TurnId.New(), "simulated-medium") { Effort = "medium" });

        Assert.Equal(["simulated-medium\u00A0·", "medium\u00A0effort"], phrase.Split(' '));
    }
}
