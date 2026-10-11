using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Notifications.Contracts;
using Avala.Notifications.Notices;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Notifications.Tests.Notices;

public sealed class NoticeRulesTests
{
    private static readonly string Item = $"item:{Samples.Session.Value:N}:tool-1";

    private static readonly string NeedsHelp = $"job:{Samples.Job.Value:N}:needs-help";

    [Fact]
    public void APermissionLeftToAPersonIsAnUrgentDecisionOfItsJobThatItsAnswerResolves()
    {
        var notice = Outcomes.Present(NoticeRules.Of(Samples.Asked("tool-1")));

        Assert.Equal(
            (NotificationKind.Decision, Severity.Urgent, $"job:{Samples.Job.Value:N}:decision", Item, Samples.Job, "Command: dotnet test"),
            (notice.Kind, notice.Severity, notice.Subject, notice.Handle, Outcomes.Present(notice.Job), Outcomes.Present(notice.Detail)));
        Assert.Equal([Item], NoticeRules.Resolved(Samples.Answered("tool-1")));
    }

    [Fact]
    public void APermissionARuleAnsweredIsNoNoticeAndResolvesItsItem()
    {
        var answered = Samples.Asked("tool-1", DecisionDelivery.Answered);

        Assert.True(NoticeRules.Of(answered).IsNone);
        Assert.Equal([Item], NoticeRules.Resolved(answered));
    }

    [Fact]
    public void AnUnansweredFormLeftToAPersonIsADecisionAndItsAnswerResolvesIt()
    {
        var asked = NoticeRules.Of(Samples.Form("tool-1", DecisionDelivery.LeftToHuman, Option<FormAnswer>.None));
        var answered = Samples.Form("tool-1", DecisionDelivery.Answered, new FormAnswer(new ItemId("tool-1"), []));

        Assert.Equal(("Which database?", Item), Outcomes.Present(asked.Map(notice => (Outcomes.Present(notice.Detail), notice.Handle))));
        Assert.True(NoticeRules.Of(answered).IsNone);
        Assert.Equal([Item], NoticeRules.Resolved(answered));
    }

    [Fact]
    public void HoldsOfAJobShareOneSubjectAndBudgetOrLimitHoldsAreBudgetNotices()
    {
        var notices = new[]
        {
            NoticeRules.Of(Samples.Held(HoldReason.Stalled), Samples.Monday),
            NoticeRules.Of(Samples.Held(HoldReason.LimitNearlyReached), Samples.Monday),
            NoticeRules.Of(Samples.OverBudget()),
            Outcomes.Present(NoticeRules.Of(new JobProgressed(Samples.Job, JobStatus.NeedsHelp), Samples.Monday)),
        };

        Assert.Equal([NotificationKind.Held, NotificationKind.Budget, NotificationKind.Budget, NotificationKind.Held], notices.Select(notice => notice.Kind));
        Assert.All(notices, notice => Assert.Equal((NeedsHelp, NeedsHelp, Severity.Urgent), (notice.Subject, notice.Handle, notice.Severity)));
        Assert.Equal(["Stalled", "Limit nearly reached", "Cost 5.2 of 5"], notices.Take(3).Select(notice => Outcomes.Present(notice.Detail)));
    }

    [Fact]
    public void AJobThatFailsOrAwaitsReviewIsAnInfoNoticeAndOnlyOtherStatusesResolveIt()
    {
        var failed = Outcomes.Present(NoticeRules.Of(new JobProgressed(Samples.Job, JobStatus.Failed), Samples.Monday));
        var review = Outcomes.Present(NoticeRules.Of(new JobProgressed(Samples.Job, JobStatus.AwaitingReview), Samples.Monday));

        Assert.Equal((NotificationKind.Failed, Severity.Info), (failed.Kind, failed.Severity));
        Assert.Equal((NotificationKind.Review, Severity.Info), (review.Kind, review.Severity));
        Assert.True(NoticeRules.Of(new JobProgressed(Samples.Job, JobStatus.Running), Samples.Monday).IsNone);
        Assert.Equal([NeedsHelp, review.Handle], NoticeRules.Resolved(new JobProgressed(Samples.Job, JobStatus.Running)));
        Assert.Equal([review.Handle], NoticeRules.Resolved(new JobProgressed(Samples.Job, JobStatus.NeedsHelp)));
        Assert.Equal([NeedsHelp], NoticeRules.Resolved(new JobProgressed(Samples.Job, JobStatus.AwaitingReview)));
    }

    [Fact]
    public void AnEndedLoopNamesItsRepositoryAndWhyItEnded()
    {
        var notice = NoticeRules.Of(Samples.Ended(), Samples.Monday);

        Assert.Equal((NotificationKind.LoopEnded, "ledger-api", "Drained"), (notice.Kind, notice.Repository, Outcomes.Present(notice.Detail)));
    }

    [Fact]
    public void AJobIsTitledByTheFirstLineOfItsInstructionCutToSixtyCharacters()
    {
        var shown = JobFacts.Of("/work/ledger-api/", Samples.Instruction);
        var cut = JobFacts.Of("/work/ledger-api", new string('a', 80));

        Assert.Equal(("ledger-api", "Fix the flaky login test"), (shown.Repository, shown.Title));
        Assert.Equal((JobFacts.LongestTitle, '…'), (cut.Title.Length, cut.Title[^1]));
    }
}
