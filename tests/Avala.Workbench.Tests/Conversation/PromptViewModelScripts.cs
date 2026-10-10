using Avala.Agents.Contracts.Connections;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Conversation;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class PromptViewModelScripts
{
    [Theory]
    [InlineData("Initial", "Instruction", true, false)]
    [InlineData("Retry", "Verification feedback", false, true)]
    [InlineData("Hint", "You", true, true)]
    [InlineData("SendBack", "Sent back", true, true)]
    [InlineData("Recovery", "Resumed after a restart", false, true)]
    [InlineData("Handoff", "Handed off", false, true)]
    public void APromptSaysWhoSentItAndOnlyAFollowUpShowsItsOrigin(string origin, string phrase, bool fromPerson, bool showsOrigin) =>
        ViewModelScript.Given(new PromptViewModel(new PromptEntry("attempt:1", 1, Enum.Parse<AttemptOrigin>(origin), "Round to whole yen", Option<AttemptOutcome>.None)))
            .Then(prompt => Assert.Equal((phrase, fromPerson, showsOrigin, string.Empty), (prompt.Origin, prompt.IsFromPerson, prompt.ShowsOrigin, prompt.Outcome)));

    [Theory]
    [InlineData("Running", "running")]
    [InlineData("AwaitingCheck", "awaiting its checks")]
    [InlineData("Passed", "passed")]
    [InlineData("Rejected", "rejected")]
    [InlineData("Interrupted", "interrupted")]
    public void AnAttemptsOutcomeShowsOnItsPrompt(string outcome, string phrase) =>
        ViewModelScript.Given(new PromptViewModel(new PromptEntry("attempt:2", 2, AttemptOrigin.Retry, "tests failed", Option<AttemptOutcome>.None)))
            .When(prompt => prompt.Update(new PromptEntry("attempt:2", 2, AttemptOrigin.Retry, "tests failed", Enum.Parse<AttemptOutcome>(outcome))))
            .Then(prompt => Assert.Equal(phrase, prompt.Outcome));

    [Fact]
    public void AHandoffPromptSaysWhereTheJobWentAndWhyOnceItsRecordArrives()
    {
        var job = JobId.New();
        var handoff = new HandoffRecord(job, 2, new ConnectionName("claude-work"), new ConnectionName("claude-personal"), new LimitReason(new ConnectionName("claude-work"), "5h", 0.91, 0.9), DateTimeOffset.UnixEpoch);
        var entry = new PromptEntry("attempt:2", 2, AttemptOrigin.Handoff, "This job was handed off to you", Option<AttemptOutcome>.None);

        ViewModelScript.Given(new PromptViewModel(entry))
            .When(prompt => prompt.Update(entry with { Handoff = handoff }))
            .Then(prompt => Assert.Equal(("Handed off to claude-personal at 91% of the 5-hour window", "This job was handed off to you"), (prompt.Origin, prompt.Text)));
    }

    [Fact]
    public void ARecoveryPromptHasNoText() =>
        ViewModelScript.Given(new PromptViewModel(new PromptEntry("attempt:3", 3, AttemptOrigin.Recovery, Option<string>.None, Option<AttemptOutcome>.None)))
            .Then(prompt => Assert.Equal((3, string.Empty), (prompt.Attempt, prompt.Text)));
}
