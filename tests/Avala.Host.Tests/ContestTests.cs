using Avala.Agents.Contracts.Events;
using Avala.Budgets.Contracts;
using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Host.Tests;

public sealed class ContestTests(PublishedPlugins plugins)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheSmallestPassingDiffWinsAndTheOtherCandidatesAreDiscardedOnceItIsApprovedAsync()
    {
        await using var run = await CheckedAsync();
        var decided = run.Watch<ContestDecided>();
        var closed = run.Watch<ContestClosed>();
        var contest = await StartAsync(run, "contest", 3);

        var verdict = Outcomes.Present((await decided.UntilAsync(found => found.Contest.Contest == contest.Contest)).Contest.Verdict);
        var winner = Outcomes.Present(verdict.Winner);
        var progressed = run.Watch<JobProgressed>();
        _ = Outcomes.Succeeds(await run.Get<IJobs>().ApproveAsync(winner, Cancellation));

        Assert.Equal(["calculator.txt"], await ChangedAsync(run, winner));
        Assert.Equal(
            [(1, Option<Criterion>.Some(Criterion.DiffSize), Option<Criterion>.None), (2, Option<Criterion>.None, Option<Criterion>.None), (3, Option<Criterion>.None, Option<Criterion>.Some(Criterion.Verification))],
            verdict.Ranking.Select(standing => (standing.Place, standing.Ahead, standing.Excluded)));
        var picked = (await closed.UntilAsync(found => found.Contest.Contest == contest.Contest)).Contest;
        Assert.Equal((ContestStatus.Closed, Option<JobId>.Some(winner)), (picked.Status, picked.Picked));
        Assert.Equal([.. contest.Candidates.Select(candidate => candidate.Job).Where(job => job != winner).OrderBy(job => job.Value)], await DiscardedAsync(progressed, 2));
    }

    [Fact]
    public async Task TheWorkbenchShowsADecidedContestAsOneRowWaitingForReviewThatLeadsWithTheWinnerAsync()
    {
        await using var run = await CheckedAsync();
        var decided = run.Watch<ContestDecided>();
        var contest = await StartAsync(run, "contest", 3);
        var winner = Outcomes.Present(Outcomes.Present((await decided.UntilAsync(found => found.Contest.Contest == contest.Contest)).Contest.Verdict).Winner);
        var workbench = await run.WorkbenchAsync();

        var row = await workbench.RowAsync(winner, "ReadyForReview");

        Assert.Equal(("ReadyForReview", "3 candidates · 2 passed"), row);
        Assert.Equal("ContestRowViewModel", await run.Ui.ReadAsync(() => Assert.Single(SidebarRows.In(workbench.Sidebar, "ReadyForReview")).Kind));
    }

    [Fact]
    public async Task AClosedContestsRowKeepsHowManyCandidatesPassedAndNamesThePickedOneAsync()
    {
        await using var run = await CheckedAsync();
        var decided = run.Watch<ContestDecided>();
        var contest = await StartAsync(run, "contest", 3);
        var winner = Outcomes.Present(Outcomes.Present((await decided.UntilAsync(found => found.Contest.Contest == contest.Contest)).Contest.Verdict).Winner);
        var workbench = await run.WorkbenchAsync();

        _ = Outcomes.Succeeds(await run.Get<IJobs>().ApproveAsync(winner, Cancellation));

        var ordinal = contest.Candidates.Single(candidate => candidate.Job == winner).Ordinal;
        Assert.Equal(("Done", $"3 candidates · 2 passed · picked candidate {ordinal}"), await workbench.RowAsync(winner, "Done"));
    }

    [Fact]
    public async Task TheInspectorOfTheWinnerShowsItsPlaceAndWhyEachCandidateRankedWhereItDidAsync()
    {
        await using var run = await CheckedAsync();
        var decided = run.Watch<ContestDecided>();
        var contest = await StartAsync(run, "contest", 3);
        var winner = Outcomes.Present(Outcomes.Present((await decided.UntilAsync(found => found.Contest.Contest == contest.Contest)).Contest.Verdict).Winner);
        var workbench = await run.WorkbenchAsync();

        var section = Assert.Single(await workbench.InspectAsync(winner, "ContestSectionViewModel"));

        var (fact, ranking) = await run.Ui.ReadAsync(() => (section["Fact"].Text, section["Ranking"].Value<IReadOnlyList<string>>()));
        Assert.EndsWith("of 3 · the winner", fact, StringComparison.Ordinal);
        Assert.Equal(3, ranking.Count);
        Assert.Contains("ahead of the next on a smaller diff · this job", ranking[0], StringComparison.Ordinal);
        Assert.EndsWith("· not verified", ranking[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheInspectorOfACandidateSetsItsSpendAgainstItsShareOfThePerJobCapAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            (".avala/checks.json", ReviewTests.CalculatorChecks),
            (".avala/permissions.json", ReviewTests.TestsPolicy),
            (".avala/budget.json", """{ "costPerJob": { "USD": 0.30 } }"""));
        var decided = run.Watch<ContestDecided>();
        var contest = await StartAsync(run, "contest", 3);
        var winner = Outcomes.Present(Outcomes.Present((await decided.UntilAsync(found => found.Contest.Contest == contest.Contest)).Contest.Verdict).Winner);
        var workbench = await run.WorkbenchAsync();

        var section = Assert.Single(await workbench.InspectAsync(winner, "UsageSectionViewModel"));

        Assert.EndsWith("of 0.10 USD", await run.Ui.ReadAsync(() => section["Fact"].Text), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheReviewOfTheWinnerOffersEveryCandidateWithItsVerificationDiffSizeAndCostAsync()
    {
        await using var run = await CheckedAsync();
        var decided = run.Watch<ContestDecided>();
        var contest = await StartAsync(run, "contest", 3);
        var winner = Outcomes.Present(Outcomes.Present((await decided.UntilAsync(found => found.Contest.Contest == contest.Contest)).Contest.Verdict).Winner);
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(winner);
        await workbench.ShowsAsync(() => ((System.Windows.Input.ICommand)workbench.Page["OpenReviewCommand"].Target).CanExecute(null));

        await run.Ui.RunAsync(() => workbench.Page.ExecuteAsync("OpenReviewCommand"));

        var choices = await run.Ui.ReadAsync(() => workbench.Page["Candidates"]["Choices"].Items.Select(choice => (choice["Facts"].Text, choice["IsOpen"].Value<bool>())).ToList());
        Assert.Equal(3, choices.Count);
        Assert.StartsWith("verified · 1 line changed · ", choices.Single(choice => choice.Item2).Item1, StringComparison.Ordinal);
        Assert.Single(choices, choice => choice.Item1.StartsWith("not verified", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApprovingTheRunnerUpPicksItAndDiscardsTheWinnerAsync()
    {
        await using var run = await CheckedAsync();
        var decided = run.Watch<ContestDecided>();
        var closed = run.Watch<ContestClosed>();
        var contest = await StartAsync(run, "contest", 3);
        var verdict = Outcomes.Present((await decided.UntilAsync(found => found.Contest.Contest == contest.Contest)).Contest.Verdict);
        var runnerUp = verdict.Ranking[1].Facts.Job;
        var progressed = run.Watch<JobProgressed>();

        _ = Outcomes.Succeeds(await run.Get<IJobs>().ApproveAsync(runnerUp, Cancellation));

        Assert.Equal(Option<JobId>.Some(runnerUp), (await closed.UntilAsync(found => found.Contest.Contest == contest.Contest)).Contest.Picked);
        Assert.Contains(Outcomes.Present(verdict.Winner), await DiscardedAsync(progressed, 2));
        Assert.Contains("CALCULATOR.md", await ChangedAsync(run, runnerUp));
    }

    [Fact]
    public async Task AContestWhoseCandidatesAllFailTheirChecksIsLeftUndecidedWithEveryCandidateRankedAsync()
    {
        await using var run = await CheckedAsync();
        var undecided = run.Watch<ContestUndecided>();
        var contest = await StartAsync(run, "contest-none", 2);

        var left = (await undecided.UntilAsync(found => found.Contest.Contest == contest.Contest)).Contest;

        var verdict = Outcomes.Present(left.Verdict);
        Assert.Equal((ContestStatus.Undecided, Option<JobId>.None), (left.Status, verdict.Winner));
        Assert.All(verdict.Ranking, standing => Assert.Equal(Option<Criterion>.Some(Criterion.Verification), standing.Excluded));
        Assert.All(left.Candidates, candidate => Assert.Equal(CandidateOutcome.NeedsHelp, candidate.Outcome));
    }

    [Fact]
    public async Task ARunThatRewroteItsChecksIsDisqualifiedEvenWithTheSmallestDiffAsync()
    {
        await using var run = await CheckedAsync();
        var decided = run.Watch<ContestDecided>();
        var contest = await StartAsync(run, "contest-rewrite", 2);

        var verdict = Outcomes.Present((await decided.UntilAsync(found => found.Contest.Contest == contest.Contest)).Contest.Verdict);

        var rewrote = Assert.Single(verdict.Ranking, standing => standing.Excluded.IsSome);
        Assert.Equal((Option<Criterion>.Some(Criterion.RuleFiles), true), (rewrote.Excluded, rewrote.Facts.Passed));
        Assert.Contains(".avala/checks.json", await ChangedAsync(run, rewrote.Facts.Job));
        Assert.Contains("CALCULATOR.md", await ChangedAsync(run, Outcomes.Present(verdict.Winner)));
    }

    [Fact]
    public async Task EachCandidateIsHeldOnceItSpendsPastItsShareOfThePerJobCapAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            (".avala/checks.json", ReviewTests.CalculatorChecks),
            (".avala/budget.json", """{ "costPerJob": { "USD": 0.04 } }"""));
        var interventions = run.Watch<BudgetIntervened>();
        var carved = run.Watch<BudgetCarved>();
        var (held, carving) = (0, 0);

        var contest = await StartAsync(run, "contest-none", 2, attempts: 3);

        var holds = await interventions.CollectUntilAsync(_ => ++held == 2);
        var carves = await carved.CollectUntilAsync(_ => ++carving == 2);
        Assert.Equal(
            [.. contest.Candidates.Select(candidate => candidate.Job).OrderBy(job => job.Value)],
            holds.Select(hold => hold.Intervention.Hold.Job).OrderBy(job => job.Value));
        Assert.All(holds, hold => Assert.Equal(
            (HoldReason.BudgetExceeded, BudgetMeasure.Cost, 0.030m, 0.020m),
            (hold.Intervention.Hold.Reason, hold.Intervention.Breach.Measure, hold.Intervention.Breach.Measured, hold.Intervention.Breach.Cap)));
        Assert.All(carves, carve => Assert.Equal((new JobId(contest.Contest.Value), 0.5, true), (carve.Carve.Parent, carve.Carve.Share, carve.Carve.Contested)));
    }

    [Fact]
    public async Task AContestStoppedWhileItWasBeingJudgedIsJudgedAgainAfterTheRestartAsync()
    {
        await using var run = await CheckedAsync();
        var judging = run.Judging.HoldNextAsync();
        var contest = await StartAsync(run, "contest", 3);
        await judging;
        var before = Outcomes.Present(run.Get<IContests>().Of(contest.Contest)).Status;
        EventWatch<ContestDecided>? decided = null;

        await run.RestartAsync(() => decided = run.Watch<ContestDecided>());

        var verdict = Outcomes.Present((await decided!.UntilAsync(found => found.Contest.Contest == contest.Contest)).Contest.Verdict);
        Assert.Equal(ContestStatus.Judging, before);
        Assert.Equal(["calculator.txt"], await ChangedAsync(run, Outcomes.Present(verdict.Winner)));
        Assert.Equal(ContestStatus.Decided, Outcomes.Present(run.Get<IContests>().Of(contest.Contest)).Status);
    }

    [Fact]
    public async Task AlwaysTheProgramInThisContestAnswersTheSameRequestOfEveryCandidateAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins);
        var decisions = run.Watch<PermissionDecided>();
        var contest = await StartAsync(run, "permission", 2);
        var waiting = 0;
        var asked = (await decisions.CollectUntilAsync(decided => decided.Decision.Delivery == DecisionDelivery.LeftToHuman && ++waiting == 2))
            .Select(decided => decided.Decision)
            .First(decision => decision.Delivery == DecisionDelivery.LeftToHuman);

        _ = Outcomes.Succeeds(await run.Get<IPermissionAnswers>().AnswerAsync(
            asked.Session,
            new PermissionReply(asked.Item, PermissionAnswer.Allow) { Remember = Remember.ForThisContest },
            Cancellation));

        var candidates = contest.Candidates.Select(candidate => candidate.Job).ToArray();
        Assert.Equal([JobStatus.AwaitingReview, JobStatus.AwaitingReview], await run.SettledAsync(candidates));
        Assert.All(candidates, candidate => Assert.Equal(
            [(PermissionAnswer.Allow, "dotnet ef database update", "always dotnet in this contest")],
            run.Get<IPermissionAudit>().AnswersOfJob(candidate).Select(answer => (answer.Answer, answer.Target, answer.Rule.Match(rule => rule.Name, () => string.Empty)))));
    }

    internal static async Task<ContestRecord> StartAsync(SimulatedRun run, string scenario, int candidates, int attempts = 1) =>
        Outcomes.Succeeds(await run.Get<IContests>().StartAsync(
            new ContestRequest(new JobRequest(run.Repository.Path, $"[simulate: {scenario}] Fix the calculator", attempts), candidates),
            Cancellation));

    internal static async Task<IReadOnlyList<string>> ChangedAsync(SimulatedRun run, JobId job)
    {
        var history = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation));
        var diff = Outcomes.Succeeds(await run.Get<IWorkspaceChanges>().DiffAsync(Outcomes.Present(history.Summary.Workspace), Cancellation));

        return [.. diff.Files.Select(file => file.Path).Order(StringComparer.Ordinal)];
    }

    private Task<SimulatedRun> CheckedAsync() =>
        SimulatedRun.PreparedAsync(plugins, (".avala/checks.json", ReviewTests.CalculatorChecks), (".avala/permissions.json", ReviewTests.TestsPolicy));

    private static async Task<IReadOnlyList<JobId>> DiscardedAsync(EventWatch<JobProgressed> progressed, int count)
    {
        var discarded = 0;

        return [.. (await progressed.CollectUntilAsync(update => update.Status == JobStatus.Discarded && ++discarded == count))
            .Where(update => update.Status == JobStatus.Discarded)
            .Select(update => update.Job)
            .OrderBy(job => job.Value)];
    }
}
