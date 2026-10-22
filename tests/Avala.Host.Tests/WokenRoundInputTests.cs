using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class WokenRoundInputTests(PublishedPlugins plugins)
{
    private const string Orchestrating = """{ "approval": "pull-request", "delegation": {}, "pullRequest": { "forge": "local", "onPullRequest": "wake-on-ci", "maxWakeUps": 3, "redeliver": "automatic" } }""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task APersonsAnswerToTheAgentsQuestionInAWokenFixComesBackForReviewInsteadOfBeingPushedAsync()
    {
        var (forge, forms) = await ForgeRun.SupervisedAsync<FormDecided>(plugins, ForgeRun.Rules("local", "wake-on-ci", 3), "ci-fails-once", "plan-approval");
        await using var owned = forge;
        await AnswerAsync(forge, await forms.UntilAsync(_ => true), "Proceed.");
        _ = await forge.ProgressedAsync(JobStatus.AwaitingReview);
        _ = await forge.ApprovedAsync();
        _ = await forge.PollUntilWokenAsync();

        await AnswerAsync(forge, await forms.UntilAsync(_ => true), "Proceed, and also delete the old calculator tests.");
        _ = await forge.ProgressedAsync(JobStatus.AwaitingReview);
        await forge.Run.DeliveredAsync();
        _ = await forge.Run.Get<IPullRequests>().RefreshAsync(forge.Job, Cancellation);

        Assert.Equal(JobStatus.AwaitingReview, await forge.StatusAsync());
    }

    [Fact]
    public async Task APermissionThePersonAllowsInAWokenFixMakesItComeBackForReviewInsteadOfBeingPushedAsync()
    {
        var (forge, decisions) = await ForgeRun.SupervisedAsync<PermissionDecided>(plugins, ForgeRun.Rules("local", "wake-on-ci", 3), "ci-fails-once", "permission");
        await using var owned = forge;
        await AllowAsync(forge, await decisions.UntilAsync(decided => decided.Decision.Delivery == DecisionDelivery.LeftToHuman));
        _ = await forge.ProgressedAsync(JobStatus.AwaitingReview);
        _ = await forge.ApprovedAsync();
        _ = await forge.PollUntilWokenAsync();

        await AllowAsync(forge, await decisions.UntilAsync(decided => decided.Decision.Delivery == DecisionDelivery.LeftToHuman));
        _ = await forge.ProgressedAsync(JobStatus.AwaitingReview);
        await forge.Run.DeliveredAsync();
        _ = await forge.Run.Get<IPullRequests>().RefreshAsync(forge.Job, Cancellation);

        Assert.Equal(JobStatus.AwaitingReview, await forge.StatusAsync());
    }

    [Fact]
    public async Task AWokenFixWhoseChildAsksItsOrchestratorWhichThenAsksThePersonComesBackForReviewInsteadOfBeingPushedAsync()
    {
        var (forge, forms) = await ForgeRun.SupervisedAsync<FormDecided>(plugins, Orchestrating, "ci-fails-once", "fix-orchestrated");
        await using var owned = forge;
        await AnswerAsync(forge, await forms.UntilAsync(asked => asked.Decision.Job == Option<JobId>.Some(forge.Job)), "Proceed.");
        _ = await forge.ProgressedAsync(JobStatus.AwaitingReview);
        _ = await forge.ApprovedAsync();
        _ = await forge.PollUntilWokenAsync();

        var childAsked = await forms.UntilAsync(asked => asked.Decision.Job != Option<JobId>.Some(forge.Job) && asked.Decision.Delivery == DecisionDelivery.LeftToParent);
        await AnswerAsync(forge, await forms.UntilAsync(asked => asked.Decision.Job == Option<JobId>.Some(forge.Job)), "Proceed, and keep the old tests.");
        _ = await forge.ProgressedAsync(JobStatus.AwaitingReview);
        await forge.Run.DeliveredAsync();
        _ = await forge.Run.Get<IPullRequests>().RefreshAsync(forge.Job, Cancellation);

        Assert.Equal(Option<JobId>.Some(forge.Job), childAsked.Decision.Parent);
        Assert.Equal(JobStatus.AwaitingReview, await forge.StatusAsync());
    }

    [Fact]
    public async Task AChildsPermissionThePersonAllowsInAWokenFixBringsItsOrchestratorBackForReviewInsteadOfBeingPushedAsync()
    {
        var (forge, decisions) = await ForgeRun.SupervisedAsync<PermissionDecided>(plugins, Orchestrating, "ci-fails-once", "fix-child-permission");
        await using var owned = forge;
        await AllowAsync(forge, await decisions.UntilAsync(decided => decided.Decision.Job != Option<JobId>.Some(forge.Job) && decided.Decision.Delivery == DecisionDelivery.LeftToHuman));
        _ = await forge.ProgressedAsync(JobStatus.AwaitingReview);
        _ = await forge.ApprovedAsync();
        _ = await forge.PollUntilWokenAsync();

        await AllowAsync(forge, await decisions.UntilAsync(decided => decided.Decision.Job != Option<JobId>.Some(forge.Job) && decided.Decision.Delivery == DecisionDelivery.LeftToHuman));
        _ = await forge.ProgressedAsync(JobStatus.AwaitingReview);
        await forge.Run.DeliveredAsync();
        _ = await forge.Run.Get<IPullRequests>().RefreshAsync(forge.Job, Cancellation);

        Assert.Equal(JobStatus.AwaitingReview, await forge.StatusAsync());
    }

    private static async Task AllowAsync(ForgeRun forge, PermissionDecided asked) =>
        _ = Outcomes.Succeeds(await forge.Run.Get<IPermissionAnswers>().AnswerAsync(asked.Decision.Session, new PermissionReply(asked.Decision.Item, PermissionAnswer.Allow), Cancellation));

    private static async Task AnswerAsync(ForgeRun forge, FormDecided asked, string text)
    {
        var field = Assert.Single(asked.Decision.Form.Fields);
        _ = Outcomes.Succeeds(await forge.Run.Get<IAgents>().AnswerAsync(
            asked.Decision.Session,
            new FormAnswer(asked.Decision.Item, [new FieldAnswer(field.Id) { Confirmed = true, Text = text }]),
            Cancellation));
    }
}
