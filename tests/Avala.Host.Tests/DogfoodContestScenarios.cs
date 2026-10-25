using System.Globalization;
using Avala.Budgets.Contracts;
using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Verification.Contracts;
using static Avala.Host.Tests.DogfoodChecks;
using static Avala.Host.Tests.DogfoodSettings;

namespace Avala.Host.Tests;

internal static class DogfoodContestScenarios
{
    private const int Candidates = 3;

    private const string Task = "Add `clamp(value, min, max)` to calc.js, exported beside add, returning value limited to the range from min to max. Add a `node:test` test file test/clamp.test.js covering a value below, inside and above the range. Run the tests.";

    private const string InContest = "Always the program in this contest";

    public static DogfoodScenario BestOfThree { get; } = new("s13", "Best of three: three candidates run the same request, the best verified one wins, and approving it discards the others", "Contests: the Candidates stepper on New job, the carved share of the per-job cap, the verdict by evidence, the contest row, the inspector's Contest section, approving the winner, discarding and reclaiming the rest, answers given in this contest", 0.30m, TimeSpan.FromMinutes(15))
    {
        Seed = setup => new DogfoodSeed
        {
            Committed =
            [
                new(".avala/jobs.json", $$"""{ "connection": "{{Main}}", "approval": "keep", "bestOf": { "candidates": 1, "max": {{Candidates}} } }"""),
                new(".avala/checks.json", setup.Rehearsal ? DogfoodLimitScenarios.RehearsedChecks : DogfoodRepository.Checks),
            ],
        },
        Steps = async scene =>
        {
            scene.Answering = decision => InContestAsync(scene, decision);
            var first = await scene.SubmitAsync(scene.Tagged("[simulate: contest]", Task), Main, "s13", Candidates);
            var contest = (await scene.UntilAsync<ContestStarted>(found => found.Contest.Candidates.Any(candidate => candidate.Job == first), "the contest to start")).Contest;
            scene.Evidence.Note("contest", contest.Contest.ToString());

            foreach (var candidate in contest.Candidates)
            {
                scene.Evidence.Note($"submitted:s13-{candidate.Ordinal}", candidate.Job.ToString());
            }

            var judged = await scene.UntilAsync<IIntegrationEvent>(found => Of(found, contest.Contest).IsSome, "the verdict");
            var record = Of(judged, contest.Contest).Match(found => found, () => contest);
            var winner = record.Verdict.Bind(verdict => verdict.Winner);
            var lead = winner.Match(found => found, () => contest.Candidates[0].Job);
            await scene.Actions.RowAsync(lead, "row:contest", record.Status == ContestStatus.Decided ? "ReadyForReview" : "NeedsYou", "candidates");
            await SectionAsync(scene, lead, "contest");

            if (winner.IsNone)
            {
                return;
            }

            await SwitcherAsync(scene, lead);
            var mark = scene.Mark;
            await scene.Actions.ApproveAsync(lead, "s13");
            var closed = (await scene.UntilAsync<ContestClosed>(found => found.Contest.Contest == contest.Contest, "the contest to close", mark)).Contest;
            scene.Evidence.Note("closed", $"{closed.Status} picked {closed.Picked.Match(found => found.ToString(), () => "none")}");

            foreach (var other in contest.Candidates.Select(candidate => candidate.Job).Where(job => job != lead))
            {
                await scene.StatusAsync(other, "a losing candidate to be discarded", mark, JobStatus.Discarded);
                var freed = await scene.UntilAsync<WorktreeFreed>(found => found.Freed.Job == other, "a losing candidate's worktree to be freed", mark);
                scene.Evidence.Note($"freed:{other}", $"{freed.Freed.Path}\t{(Directory.Exists(freed.Freed.Path) ? "still on disk" : "gone")}");
            }

            await scene.GitAsync("worktrees", scene.Place.Repository, "worktree", "list");
            await scene.Actions.RowAsync(lead, "row:closed", "Done", "candidates");
        },
        Checks =
        [
            Fact("The person steps New job's Candidates to 3 and each may spend a third of the job's budget", "new-job:s13:candidates", text => text.StartsWith("3 candidates", StringComparison.Ordinal) && text.Contains("more offered False", StringComparison.Ordinal) && text.Contains("a third", StringComparison.Ordinal)),
            new("ContestStarted holds 3 candidate jobs, each running on claude, all on one account", evidence =>
                Started(evidence, contest =>
                {
                    var sessions = contest.Candidates.SelectMany(candidate => evidence.SessionsOf(candidate.Job)).ToList();
                    var accounts = sessions.Select(opened => opened.Account.Match(account => account.Id, () => "none")).Distinct().ToList();
                    var everyone = contest.Candidates.All(candidate => evidence.SessionsOf(candidate.Job).Any());

                    return Expect(evidence, contest.Candidates.Count == Candidates && everyone && sessions.All(opened => opened.Connection.Value == Main) && accounts.Count == 1, $"{contest.Candidates.Count} candidates, sessions [{string.Join(", ", sessions.Select(opened => $"{evidence.Name(evidence.JobOf(opened.Session))} {opened.Connection.Value}"))}], accounts [{string.Join(", ", accounts)}]");
                })),
            new("Budgets carves each candidate a third of the per-job cap, marked contested", evidence =>
                Started(evidence, contest =>
                {
                    var carves = evidence.All<BudgetCarved>(found => found.Carve.Parent.Value == contest.Contest.Value).Select(found => found.Carve).ToList();
                    var share = 0.30m / Candidates;
                    var fair = carves.Count == Candidates
                        && carves.Select(carve => carve.Child.Value).Order().SequenceEqual(contest.Candidates.Select(candidate => candidate.Job.Value).Order())
                        && carves.All(carve => carve.Contested && Math.Abs(carve.Share - (1.0 / Candidates)) < 0.001 && carve.Cost.Any(cost => cost.Currency == "USD" && Math.Abs(cost.Amount - share) < 0.001m));

                    return Expect(evidence, fair, $"carves [{string.Join(", ", carves.Select(carve => string.Create(CultureInfo.InvariantCulture, $"{evidence.Name(carve.Child)} share {carve.Share:0.###} {string.Join(" ", carve.Cost.Select(cost => $"{cost.Amount:0.####} {cost.Currency}"))} contested {carve.Contested}")))}]");
                })),
            new("Every candidate settles", evidence =>
                Started(evidence, contest =>
                {
                    var settled = evidence.All<CandidateSettled>(found => found.Contest.Contest == contest.Contest).Select(found => found.Candidate).Distinct().ToList();

                    return Expect(evidence, contest.Candidates.All(candidate => settled.Contains(candidate.Job)), $"settled [{string.Join(", ", contest.Candidates.Select(candidate => $"{evidence.Name(candidate.Job)}: {string.Join(" → ", evidence.Journey(candidate.Job))}"))}]");
                })),
            new("The contest is Decided with a winner whose verification passed", evidence =>
                Judged(evidence, (record, verdict) =>
                    verdict.Winner.Match(
                        winner =>
                        {
                            var report = evidence.All<AttemptVerified>(found => found.Report.Job == winner).Select(found => found.Report).LastOrDefault();

                            return Expect(evidence, record.Status == ContestStatus.Decided && report?.Outcome == VerificationOutcome.Passed, $"{record.Status}, winner {evidence.Name(winner)} last verified {report?.Outcome.ToString() ?? "never"}; {Standings(evidence, verdict)}");
                        },
                        () => DogfoodVerdict.NotReached($"{record.Status}: no candidate passed its checks; {Standings(evidence, verdict)}")))),
            new("The inspector's Contest section ranks every candidate as the verdict does, with its reason, and marks the winner", evidence =>
                Judged(evidence, (_, verdict) =>
                {
                    var lines = evidence.Fact("contest:contest:ranking").Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    var matches = lines.Length == verdict.Ranking.Count && verdict.Ranking.Select((standing, index) => (standing, line: lines[index])).All(pair =>
                        pair.line.StartsWith(string.Create(CultureInfo.InvariantCulture, $"{pair.standing.Place}. Candidate {pair.standing.Facts.Ordinal}"), StringComparison.Ordinal)
                        && (pair.standing.Excluded.IsSome
                            ? pair.line.Contains("not verified", StringComparison.Ordinal) || pair.line.Contains("disqualified", StringComparison.Ordinal)
                            : pair.line.Contains("ahead of the next", StringComparison.Ordinal) || pair.line.Contains("verified run", StringComparison.Ordinal)));
                    var marked = verdict.Winner.Match(_ => evidence.Fact("contest:contest:fact").EndsWith("the winner", StringComparison.Ordinal) && lines.Length > 0 && lines[0].EndsWith("this job", StringComparison.Ordinal), () => true);

                    return Expect(evidence, matches && marked, $"fact '{evidence.Fact("contest:contest:fact")}', status '{evidence.Fact("contest:contest:status")}', ranking [{string.Join(" / ", lines)}]; verdict {Standings(evidence, verdict)}");
                })),
            new("The board shows the contest as one row reading 3 candidates · N passed, N the passing candidates", evidence =>
                Judged(evidence, (_, verdict) =>
                {
                    var passed = verdict.Ranking.Count(standing => standing.Facts.Passed);
                    var row = evidence.Fact("row:contest");

                    return Expect(evidence, row.EndsWith(string.Create(CultureInfo.InvariantCulture, $": {Candidates} candidates · {passed} passed"), StringComparison.Ordinal), $"row '{row}', {passed} passed in the verdict");
                })),
            Fact("The winner's review offers every candidate in its switcher, the winner open and verified", "review:switcher", text => text.Split('\n', StringSplitOptions.RemoveEmptyEntries) is var lines && lines.Length == Candidates && lines.Count(line => line.StartsWith("open ", StringComparison.Ordinal)) == 1 && lines.Single(line => line.StartsWith("open ", StringComparison.Ordinal)).Contains("open verified", StringComparison.Ordinal)),
            new("Approving the winner from its review closes the contest with it picked and discards the other candidates", evidence =>
                Judged(evidence, (record, verdict) => verdict.Winner.Match(
                    winner =>
                    {
                        var closed = evidence.First<ContestClosed>(found => found.Contest.Contest == record.Contest);
                        var others = record.Candidates.Select(candidate => candidate.Job).Where(job => job != winner).ToList();
                        var holds = closed.Match(found => found.Contest.Status == ContestStatus.Closed && found.Contest.Picked == Option<JobId>.Some(winner), () => false)
                            && evidence.Journey(winner).Contains(JobStatus.Approved)
                            && others.All(job => evidence.Journey(job) is [.., JobStatus.Discarded]);

                        return Expect(evidence, holds, $"closed '{evidence.Fact("closed")}', row '{evidence.Fact("row:closed")}', approve '{evidence.Fact("approve:s13")}', winner {evidence.Name(winner)} [{string.Join(" → ", evidence.Journey(winner))}], others [{string.Join("; ", others.Select(job => $"{evidence.Name(job)}: {string.Join(" → ", evidence.Journey(job))}"))}]");
                    },
                    () => DogfoodVerdict.NotReached("no winner to approve")))),
            new("The discarded candidates' worktrees are reclaimed and gone from disk and from git worktree list", evidence =>
                Judged(evidence, (record, verdict) => verdict.Winner.Match(
                    winner =>
                    {
                        var others = record.Candidates.Select(candidate => candidate.Job).Where(job => job != winner).ToList();
                        var reclaimed = others.Select(job => evidence.Fact($"reclaimed:{job}")).ToList();
                        var listed = evidence.Fact("worktrees");
                        var holds = reclaimed.All(text => text.EndsWith("\tgone", StringComparison.Ordinal) && !listed.Contains(text.Split('\t')[0], StringComparison.Ordinal));

                        return Expect(evidence, holds, $"reclaimed [{string.Join("; ", reclaimed.Select(text => text.Replace('\t', ' ')))}], worktree list [{listed.Trim().ReplaceLineEndings("; ")}]");
                    },
                    () => DogfoodVerdict.NotReached("no winner was approved, so nothing was discarded")))),
            new("An answer given 'Always the program in this contest' answers the same request of every other candidate", evidence =>
            {
                var given = evidence.FactsOf("contest-answer:").Select(text => text.Split('\t')).ToList();

                if (given.Count == 0)
                {
                    return DogfoodVerdict.Pass("not exercised: no candidate's request needed an answer that could be shared");
                }

                var contest = evidence.First<ContestStarted>(_ => true).Match(found => found.Contest.Candidates.Select(candidate => candidate.Job).ToList(), () => []);
                var spread = given.Select(answer => (Target: answer[2], Others: contest.Where(job => evidence.Name(job) != answer[0]).Select(job => (Job: job, Asked: evidence.All<PermissionDecided>(found => found.Decision.Job == Option<JobId>.Some(job) && found.Decision.Target == answer[2]).Select(found => found.Decision).ToList(), Answered: evidence.All<PermissionAnswered>(found => found.Answer.Job == Option<JobId>.Some(job) && found.Answer.Target == answer[2]).ToList())).ToList())).ToList();
                var holds = spread.All(answer => answer.Others.All(other => other.Asked.Count == 0 || other.Answered.Count > 0 || other.Asked.Any(decision => decision.Delivery != DecisionDelivery.LeftToHuman && decision.Rule.Match(rule => rule.Name.EndsWith(" in this contest", StringComparison.Ordinal), () => false))));

                return Expect(evidence, holds, string.Join("; ", spread.Select(answer => $"'{answer.Target}': {string.Join(", ", answer.Others.Select(other => $"{evidence.Name(other.Job)} asked {other.Asked.Count} answered {other.Answered.Count} by [{string.Join(" ", other.Asked.Select(decision => $"{decision.Delivery}/{decision.Rule.Match(rule => rule.Name, () => "no rule")}"))}]"))}")));
            }),
        ],
    };

    private static Option<ContestRecord> Of(IIntegrationEvent happened, ContestId contest) => happened switch
    {
        ContestDecided decided when decided.Contest.Contest == contest => decided.Contest,
        ContestUndecided undecided when undecided.Contest.Contest == contest => undecided.Contest,
        _ => Option<ContestRecord>.None,
    };

    private static DogfoodVerdict Started(DogfoodEvidence evidence, Func<ContestRecord, DogfoodVerdict> judge) =>
        DogfoodVerdict.Unless(evidence.First<ContestStarted>(_ => true).Map(found => found.Contest), $"no contest started ({evidence.Outcome})", judge);

    private static DogfoodVerdict Judged(DogfoodEvidence evidence, Func<ContestRecord, ContestVerdict, DogfoodVerdict> judge) =>
        DogfoodVerdict.Unless(
            evidence.All<IIntegrationEvent>().Select(found => found switch
            {
                ContestDecided decided => Option<ContestRecord>.Some(decided.Contest),
                ContestUndecided undecided => Option<ContestRecord>.Some(undecided.Contest),
                _ => Option<ContestRecord>.None,
            }).FirstOrDefault(found => found.IsSome, Option<ContestRecord>.None),
            $"the contest was never judged ({evidence.Outcome})",
            record => DogfoodVerdict.Unless(record.Verdict, "the judged contest holds no verdict", verdict => judge(record, verdict)));

    private static string Standings(DogfoodEvidence evidence, ContestVerdict verdict) =>
        string.Join(" / ", verdict.Ranking.Select(standing => string.Create(CultureInfo.InvariantCulture, $"{standing.Place}. {evidence.Name(standing.Facts.Job)} (candidate {standing.Facts.Ordinal}) passed {standing.Facts.Passed}, {standing.Facts.Attempts} attempts, {standing.Facts.LinesChanged} lines, excluded {standing.Excluded.Match(found => found.ToString(), () => "no")}, ahead on {standing.Ahead.Match(found => found.ToString(), () => "nothing")}")));

    private static async Task SectionAsync(DogfoodScene scene, JobId job, string label)
    {
        var section = await scene.Actions.SectionAsync(job, "ContestSectionViewModel", label);
        scene.Evidence.Note($"contest:{label}:fact", section["Fact"].Text);
        scene.Evidence.Note($"contest:{label}:status", section["Status"].Text);
        scene.Evidence.Note($"contest:{label}:ranking", string.Join('\n', section["Ranking"].Value<IReadOnlyList<string>>()));
    }

    private static async Task SwitcherAsync(DogfoodScene scene, JobId job)
    {
        await scene.Actions.OpenReviewAsync(job, "s13-switcher");
        var jobs = scene.App.Ui.Jobs;
        var choices = jobs["Candidates"]["Choices"].Items.Select(choice => $"{(choice["IsOpen"].Value<bool>() ? "open" : "other")} {choice["Facts"].Text}");
        scene.Evidence.Note("review:switcher", string.Join('\n', choices));
        DogfoodUi.Invoke(jobs, "CloseReviewCommand");
    }

    private static async Task<bool> InContestAsync(DogfoodScene scene, PolicyDecision decision)
    {
        var job = decision.Job.Match(found => found, () => default);
        var (allow, _) = DogfoodPolicy.Judge(decision.Kind, decision.Target, scene.WorktreeOf(job));

        if (!decision.Contested || !allow)
        {
            return false;
        }

        if (scene.App.Get<IPermissionAudit>().AnswersOfJob(job).Any(answer => answer.Item == decision.Item))
        {
            await scene.Journal.NoteAsync($"SHARED: {scene.Evidence.Name(job)}'s request '{decision.Target}' was already answered for the whole contest");

            return true;
        }

        scene.Evidence.Note($"contest-answer:{decision.Item}", $"{scene.Evidence.Name(job)}\t{decision.Kind}\t{decision.Target}");

        return await scene.Actions.AnswerPermissionAsync(decision, true, InContest, DogfoodRemember.ForThisJob);
    }
}
