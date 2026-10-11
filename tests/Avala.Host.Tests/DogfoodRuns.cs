using Avala.Testing.UI;

namespace Avala.Host.Tests;

public sealed class DogfoodRuns(HeadlessUi ui, PublishedPlugins plugins)
{
    private DogfoodCampaign Campaign => new(plugins, ui);

    [Fact]
    public Task AJobIsVerifiedReviewedAndApprovedWithItsCostLabelledAtApiPricesAsync() => Campaign.RunAsync(DogfoodJobScenarios.Verified);

    [Fact]
    public Task AlwaysInThisRepositoryTurnsOneAnswerIntoACommittedRuleTheNextJobObeysAsync() => Campaign.RunAsync(DogfoodJobScenarios.AlwaysInRepository);

    [Fact]
    public Task AJobHeldForInvalidChecksIsResubmittedAfterTheFixIsCommittedAsync() => Campaign.RunAsync(DogfoodJobScenarios.Resubmitted);

    [Fact]
    public Task AJobOverItsCostCapIsHeldThenRaisedAndContinuedFromTheInspectorAsync() => Campaign.RunAsync(DogfoodJobScenarios.Raised);

    [Fact]
    public Task TheMachinesDailySpendCeilingHoldsARunningJobAsync() => Campaign.RunAsync(DogfoodJobScenarios.DailyCeiling);

    [Fact]
    public Task AJobNearItsUsageWindowIsHeldWaitsForTheResetAndKeepsItsHoldAcrossARestartAsync() => Campaign.RunAsync(DogfoodLimitScenarios.HeldNearLimit);

    [Fact]
    public Task AJobStartedOnAnExhaustedAccountIsHeldForItsLimitAndHandedOffToTheOtherAccountWhereItPassesAsync() => Campaign.RunAsync(DogfoodLimitScenarios.HandedOff);

    [Fact]
    public Task ASignedWebhookStartsAJobCappedAtTheRepositorysAutonomyAndBadDeliveriesStartNothingAsync() => Campaign.RunAsync(DogfoodAutomationScenarios.Webhook);

    [Fact]
    public Task ALoopRunsUnderTheMachinesLimitsApprovesCleanEvidenceAndSurvivesARestartAsync() => Campaign.RunAsync(DogfoodAutomationScenarios.Loop);

    [Fact]
    public Task AnApprovedJobsPullRequestIsWatchedAndTheAgentIsWokenByFailingCiAsync() => Campaign.RunAsync(DogfoodAutomationScenarios.PullRequest);

    [Fact]
    public Task AnOrchestratorDelegatesToAChildOnTheSameAccountWhichAsksItAndAReviewerReportsAsync() => Campaign.RunAsync(DogfoodTeamScenarios.Orchestrated);

    [Fact]
    public Task ARestartWhileTheChecksRunKeepsTheConversationAndLeavesNoCheckRunningAsync() => Campaign.RunAsync(DogfoodTeamScenarios.RestartedWhileChecking);
}
