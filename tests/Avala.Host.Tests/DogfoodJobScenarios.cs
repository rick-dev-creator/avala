using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Verification.Contracts;
using static Avala.Host.Tests.DogfoodChecks;
using static Avala.Host.Tests.DogfoodSettings;

namespace Avala.Host.Tests;

internal static class DogfoodJobScenarios
{
    public const string Task = "Add `sub(a, b)` to calc.js and a `node:test` test file test/calc.test.js covering add and sub. Run the tests.";

    private const string Demo = "console.log(require('./calc').add(2, 3));\n";

    public static DogfoodScenario Verified { get; } = new("s1", "A job is verified, reviewed and approved, with its cost labelled at API prices", "job flow, verification with evidence, the review sheet, the API-equivalent cost basis", 0.20m, TimeSpan.FromMinutes(15))
    {
        Seed = _ => new DogfoodSeed { Recordings = [Calculator("s1")] },
        Steps = async scene =>
        {
            var job = await scene.SubmitAsync(scene.Tagged("[replay: s1]", Task), Main, "s1");

            if (await scene.SettledAsync(job, "the job to settle") != JobStatus.AwaitingReview)
            {
                return;
            }

            await scene.GitAsync("main:before", scene.Place.Repository, "rev-parse", "main");
            await scene.GitAsync("worktree:changes", scene.WorktreeOf(job), "diff", "--stat", "main");
            await scene.Actions.ConversationAsync(job, "conversation:s1");
            await scene.Actions.InspectAsync(job, "review");
            await scene.Actions.ApproveAsync(job, "s1");
            await scene.StatusAsync(job, "the approval", 0, JobStatus.Approved);
            await scene.GitAsync("main:after", scene.Place.Repository, "rev-parse", "main");
            await scene.GitAsync("branches", scene.Place.Repository, "branch", "--all", "--verbose");
        },
        Checks =
        [
            Journey("JobProgressed goes Running → Checking → AwaitingReview → Approved", "s1", JobStatus.Running, JobStatus.Checking, JobStatus.AwaitingReview, JobStatus.Approved),
            Verified("AttemptVerified reports tests Passed with exit code 0", "s1", "tests"),
            new("The review sheet lists calc.js and test/calc.test.js with counts and says verified", evidence =>
                Expect(evidence, evidence.Fact("review:s1:verified") == "True" && evidence.Fact("review:s1:files").Contains("calc.js", StringComparison.Ordinal) && evidence.Fact("review:s1:files").Contains("test/calc.test.js", StringComparison.Ordinal), $"verified {evidence.Fact("review:s1:verified")}, files [{evidence.Fact("review:s1:files").ReplaceLineEndings("; ")}]")),
            Priced("Every UsageReported cost is API-equivalent and the inspector Usage reads … USD at API prices"),
            Fact("The turn end in the conversation reads … at API prices", "conversation:s1", text => text.Contains("at API prices", StringComparison.Ordinal)),
            new("The job's worktree holds the change and the base branch in repository/ is untouched", evidence =>
                Expect(evidence, evidence.Fact("worktree:changes").Contains("calc.js", StringComparison.Ordinal) && evidence.Has("main:after") && evidence.Fact("main:before") == evidence.Fact("main:after"), $"worktree [{evidence.Fact("worktree:changes").ReplaceLineEndings("; ")}], main {evidence.Fact("main:before").Trim()} → {evidence.Fact("main:after").Trim()}")),
        ],
    };

    public static DogfoodScenario AlwaysInRepository { get; } = new("s2", "Always in this repository turns one answer into a committed rule that the next job obeys", "job rules, the repository rule written through the validated path, the card's notice", 0.20m, TimeSpan.FromMinutes(15))
    {
        Seed = _ => new DogfoodSeed
        {
            Committed = [new("demo.js", Demo)],
            Recordings = [new DogfoodScript("s2").Turn().Ask("node demo.js", "5").Say("It prints 5.").Bill(0.0081m, 0.22).Finish()],
        },
        Steps = async scene =>
        {
            scene.Answering = decision => AlwaysAsync(scene, decision);
            var first = await scene.SubmitAsync(scene.Tagged("[replay: s2]", "Run exactly `node demo.js` once and tell me what it prints. Do not change any file."), Main, "s2-first");
            await scene.SettledAsync(first, "the first job to settle");
            await scene.ReadAsync("permissions:working", Path.Combine(scene.Place.Repository, ".avala", "permissions.json"));
            await scene.Actions.InspectAsync(first, "first");
            await DogfoodRepository.CommitAsync(scene.Place, "Commit the rule written by Always in this repository");
            scene.Answering = null;
            var second = await scene.SubmitAsync(scene.Tagged("[replay: s2]", "Run exactly `node demo.js` and report the output."), Main, "s2-second");
            await scene.SettledAsync(second, "the second job to settle");
        },
        Checks =
        [
            OfJob("Job 1's PolicyDecision is LeftToHuman with a RepositoryRule naming node demo.js", "s2-first", (evidence, job) =>
            {
                var asked = evidence.All<PermissionDecided>(decided => decided.Decision.Job == Option<JobId>.Some(job) && decided.Decision.Delivery == DecisionDelivery.LeftToHuman).Select(decided => decided.Decision).ToList();
                var ruled = asked.FirstOrDefault(decision => decision.RepositoryRule.Bind(rule => rule.Target).Match(target => target == "node demo.js", () => false));

                return asked.Count == 0 ? DogfoodVerdict.NotReached("job 1 asked nothing of a person")
                    : ruled is not null ? DogfoodVerdict.Pass($"asked [{string.Join(", ", asked.Select(decision => decision.Target))}] with a repository rule for node demo.js")
                    : DogfoodVerdict.NotReached($"not offered: job 1 asked [{string.Join(", ", asked.Select(decision => decision.Target))}], none of which is exactly node demo.js, so no exact repository rule exists");
            }),
            Fact("The card says it was added to .avala/permissions.json and applies to new jobs once committed", "card:node demo.js", text => text.Contains("Added to .avala/permissions.json. It applies to new jobs once you commit it; this job won't ask again.", StringComparison.Ordinal)),
            Fact("The working file gained a rule named 'always in this repository' with the exact target", "permissions:working", text => text.Contains("always in this repository", StringComparison.Ordinal) && text.Contains("\"node demo.js\"", StringComparison.Ordinal)),
            OfJob("Job 2's request is allowed by the repository rule 'always in this repository' and nothing is left to a person", "s2-second", (evidence, job) =>
            {
                var decisions = evidence.All<PermissionDecided>(decided => decided.Decision.Job == Option<JobId>.Some(job)).Select(decided => decided.Decision).ToList();
                var demo = decisions.FirstOrDefault(decision => decision.Target == "node demo.js");

                return demo is null
                    ? DogfoodVerdict.NotReached($"job 2 asked [{string.Join(", ", decisions.Select(decision => decision.Target))}]")
                    : Expect(evidence, demo.Answer == PolicyAnswer.Allow && demo.Rule.Match(rule => rule.Name == "always in this repository" && rule.Origin == RuleOrigin.Repository, () => false) && decisions.All(decision => decision.Delivery != DecisionDelivery.LeftToHuman), $"node demo.js {demo.Answer} by {demo.Rule.Match(rule => $"{rule.Origin} rule '{rule.Name}'", () => "no rule")}, deliveries [{string.Join(", ", decisions.Select(decision => decision.Delivery))}]");
            }),
            new("The inspector of job 1 lists the answer with the rule it created", evidence =>
                Expect(evidence, evidence.FactsOf("inspector:first:").Any(text => text.Contains("node demo.js", StringComparison.Ordinal) && text.Contains("always in this repository", StringComparison.Ordinal)), Short(string.Join(" / ", evidence.FactsOf("inspector:first:").Where(text => text.Contains("node demo.js", StringComparison.Ordinal)))))),
        ],
    };

    public static DogfoodScenario Resubmitted { get; } = new("s3", "A job held for invalid checks is resubmitted after the person fixes and commits the file", "the InvalidChecks hold, the review sheet advice, Resubmit, rules read from the base commit", 0.20m, TimeSpan.FromMinutes(15))
    {
        Seed = _ => new DogfoodSeed
        {
            Committed = [new(".avala/checks.json", """{ "checks": [ { "name": "tests" } ] }""")],
            Recordings = [Calculator("s3")],
        },
        Steps = async scene =>
        {
            var held = await scene.SubmitAsync(scene.Tagged("[replay: s3]", Task), Main, "s3");
            await scene.UntilAsync<JobHeld>(found => found.Hold.Job == held, "the hold for invalid checks");
            await scene.SettledAsync(held, "the held job to settle");
            await scene.Actions.RowAsync(held, "row:held", "NeedsYou", "held: invalid checks");
            await DogfoodRepository.CommitAsync(scene.Place, ".avala/checks.json", DogfoodRepository.Checks, "Fix the checks");
            var fresh = await scene.Actions.ResubmitAsync(held, "s3");
            scene.Evidence.Note("submitted:s3-resubmitted", fresh.ToString());
            await scene.StatusAsync(held, "the held job to be discarded", 0, JobStatus.Discarded);
            await scene.SettledAsync(fresh, "the resubmitted job to settle");
            var listed = await scene.App.Get<IJobCatalog>().ListAsync(Cancellation);

            foreach (var (label, found) in (ValueTuple<string, JobId>[])[("held", held), ("fresh", fresh)])
            {
                scene.Evidence.Note($"catalog:{label}", listed.Where(summary => summary.Job == found).Select(summary => $"{summary.Status}\t{summary.Connection.Match(connection => connection.Value, () => "none")}\t{summary.Model}\t{summary.Instruction}").FirstOrDefault() ?? string.Empty);
            }
        },
        Checks =
        [
            OfJob("The job is held InvalidChecks and the sidebar says held: invalid checks", "s3", (evidence, job) =>
                Expect(evidence, evidence.All<JobHeld>(found => found.Hold.Job == job && found.Hold.Reason == HoldReason.InvalidChecks).Any() && evidence.Fact("row:held").Contains("held: invalid checks", StringComparison.Ordinal), $"holds [{string.Join(", ", evidence.All<JobHeld>(found => found.Hold.Job == job).Select(found => found.Hold.Reason))}], row '{evidence.Fact("row:held")}'")),
            Fact("The review sheet advises fixing and committing .avala/checks.json, then resubmitting", "review:s3:advice", text => text.StartsWith("Fix .avala/checks.json and commit it", StringComparison.Ordinal)),
            Fact("The review sheet offers Resubmit", "review:s3:resubmit", text => text == "True"),
            new("A new job with the same instruction, connection and model is submitted and the held one is Discarded", evidence =>
            {
                var (held, fresh) = (evidence.Fact("catalog:held").Split('\t'), evidence.Fact("catalog:fresh").Split('\t'));

                return held.Length < 4 || fresh.Length < 4
                    ? DogfoodVerdict.NotReached($"the catalog was not read ({evidence.Outcome})")
                    : Expect(evidence, held[0] == nameof(JobStatus.Discarded) && held.Skip(1).SequenceEqual(fresh.Skip(1)), $"held [{string.Join(" | ", held)}]; new [{string.Join(" | ", fresh)}]");
            }),
            Verified("The new job's verification runs node --test and passes", "s3-resubmitted", "tests"),
            Journey("The new job reaches AwaitingReview", "s3-resubmitted", JobStatus.AwaitingReview),
            Fact("The workbench shows the new job's conversation", "resubmit:s3", text => text.Contains("workbench shows", StringComparison.Ordinal)),
        ],
    };

    public static DogfoodScenario Raised { get; } = new("s4", "A job over its cost cap is held at the end of its turn, then raised and continued from the inspector", "the spend gate after each turn, BudgetExceeded, the hold phrase at API prices, Raise budget and continue, the second hold audited", 0.15m, TimeSpan.FromMinutes(15))
    {
        Seed = _ => new DogfoodSeed
        {
            Committed = [new(".avala/budget.json", """{ "costPerJob": { "USD": 0.001 } }""")],
            Recordings =
            [
                new DogfoodScript("s4").Turn().Say("Writing sub first.").Write("calc.js", Sub).Say("sub is written; the tests come next.").Bill(0.0150m, 0.22).Finish()
                    .Turn().Write("test/calc.test.js", SubTests).Run("node --test", "# pass 2").Say("Added the tests; node --test passes.").Bill(0.0040m, 0.23).Finish(),
            ],
        },
        Steps = async scene =>
        {
            var job = await scene.SubmitAsync(scene.Tagged("[replay: s4]", Task), Main, "s4");
            await scene.UntilAsync<BudgetIntervened>(found => found.Intervention.Hold.Job == job, "the budget hold");
            await scene.SettledAsync(job, "the held job to settle");
            var usage = await scene.Actions.SectionAsync(job, "UsageSectionViewModel", "held");
            await scene.App.Ui.UntilAsync(usage.Presentation, () => usage["CanRaise"].Value<bool>(), "Raise budget and continue to be offered");
            var mark = scene.Mark;
            await usage.ExecuteAsync("RaiseCommand");
            await scene.UntilAsync<BudgetRaised>(found => found.Raise.Job == job, "the raise");
            var settled = await scene.UntilAsync<IIntegrationEvent>(found => found switch
            {
                JobProgressed progressed => progressed.Job == job && progressed.Status == JobStatus.AwaitingReview,
                BudgetIntervened held => held.Intervention.Hold.Job == job && scene.Evidence.All<BudgetIntervened>(again => again.Intervention.Hold.Job == job).Count() > 1,
                _ => false,
            }, "review or a second hold", mark);
            await scene.Actions.ConversationAsync(job, "conversation:s4");
            await scene.Actions.InspectAsync(job, "raised");
            scene.Evidence.Note("s4:after-raise", settled is JobProgressed reviewed ? reviewed.Status.ToString() : "held again");
        },
        Checks =
        [
            OfJob("The job is held BudgetExceeded with one BudgetIntervened whose breach is Cost", "s4", (evidence, job) =>
            {
                var first = evidence.All<BudgetIntervened>(found => found.Intervention.Hold.Job == job).Select(found => found.Intervention).FirstOrDefault();

                return first is null ? DogfoodVerdict.NotReached($"no budget intervention ({evidence.Outcome})")
                    : Expect(evidence, first.Hold.Reason == HoldReason.BudgetExceeded && first.Breach.Measure == BudgetMeasure.Cost, $"held {first.Hold.Reason} for {first.Breach.Measure} {first.Breach.Measured} of {first.Breach.Cap}");
            }),
            Fact("The inspector reads Held for Cost … at API prices of 0.001 USD and offers Raise budget and continue", "inspector:held:UsageSectionViewModel", text => text.Contains("at API prices", StringComparison.Ordinal) && text.Contains("0.001 USD", StringComparison.Ordinal) && text.Contains("CanRaise=True", StringComparison.Ordinal)),
            OfJob("BudgetRaised is published for the job", "s4", (evidence, job) =>
                Unless(evidence.First<BudgetRaised>(found => found.Raise.Job == job), "no raise", raised => DogfoodVerdict.Pass($"raised by {string.Join(", ", raised.Raise.By.Cost.Select(cost => $"{cost.Amount} {cost.Currency}"))}"))),
            Fact("The conversation shows that the budget was raised and the agent carries on", "conversation:s4", text => text.Contains("budget was raised", StringComparison.OrdinalIgnoreCase)),
            OfJob("The job reaches review, or is held again with a second intervention audited", "s4", (evidence, job) =>
            {
                var holds = evidence.All<BudgetIntervened>(found => found.Intervention.Hold.Job == job).Count();
                var reviewed = evidence.Journey(job).Contains(JobStatus.AwaitingReview);

                return Expect(evidence, reviewed || holds > 1, $"after the raise: {evidence.Fact("s4:after-raise")}, {holds} interventions");
            }),
            new("The inspector lists the raise", evidence => Expect(evidence, evidence.Fact("inspector:raised:UsageSectionViewModel") is var text && DogfoodChecks.Between(text, "Raises=[", "]").Length > 0, Short(evidence.Fact("inspector:raised:UsageSectionViewModel")))),
        ],
    };

    public static DogfoodScenario DailyCeiling { get; } = new("s5", "The machine's daily spend ceiling holds the job whose turn crosses it before its checks run", "budgets.json spendPerDay, the DailySpend breach, the hold phrase on this machine", 0.05m, TimeSpan.FromMinutes(15))
    {
        Seed = _ => new DogfoodSeed
        {
            Committed = [new(".avala/budget.json", string.Empty)],
            Data = [new("budgets.json", """{ "spendPerDay": { "USD": 0.001 } }""")],
            Recordings = [new DogfoodScript("s5").Turn().Say("Starting on sub.").Write("calc.js", Sub).Say("sub is written.").Bill(0.0120m, 0.22).Finish()],
        },
        Steps = async scene =>
        {
            var job = await scene.SubmitAsync(scene.Tagged("[replay: s5]", Task), Main, "s5");
            await scene.UntilAsync<BudgetIntervened>(found => found.Intervention.Hold.Job == job, "the daily ceiling hold");
            await scene.SettledAsync(job, "the held job to settle");
            await scene.Actions.InspectAsync(job, "held");
        },
        Checks =
        [
            OfJob("The running job is held BudgetExceeded with a DailySpend breach on no connection", "s5", (evidence, job) =>
                Unless(evidence.First<BudgetIntervened>(found => found.Intervention.Hold.Job == job), "no budget intervention", found => Expect(evidence, found.Intervention.Hold.Reason == HoldReason.BudgetExceeded && found.Intervention.Breach.Measure == BudgetMeasure.DailySpend && found.Intervention.Breach.Connection.IsNone, $"held {found.Intervention.Hold.Reason} for {found.Intervention.Breach.Measure} {found.Intervention.Breach.Measured} of {found.Intervention.Breach.Cap}, connection {found.Intervention.Breach.Connection.Match(connection => connection.Value, () => "none")}"))),
            OfJob("The turn that crossed the ceiling is held before its checks run: never verified, never awaiting review", "s5", (evidence, job) =>
            {
                var journey = evidence.Journey(job);
                var verified = evidence.All<AttemptVerified>(found => found.Report.Job == job).Count();

                return Expect(evidence, verified == 0 && !journey.Contains(JobStatus.AwaitingReview) && journey is [.., JobStatus.NeedsHelp], $"journey [{string.Join(" → ", journey)}], {verified} verifications");
            }),
            Fact("The inspector reads Held for today's spend on this machine: x of 0.001 USD", "inspector:held:UsageSectionViewModel", text => text.Contains("today's spend on this machine", StringComparison.Ordinal) && text.Contains("0.001 USD", StringComparison.Ordinal)),
            Fact("Raise is not offered", "inspector:held:UsageSectionViewModel", text => text.Contains("CanRaise=False", StringComparison.Ordinal)),
        ],
    };

    private static DogfoodVerdict Unless<T>(Option<T> found, string missing, Func<T, DogfoodVerdict> judge)
        where T : notnull => DogfoodVerdict.Unless(found, missing, judge);

    private static async Task<bool> AlwaysAsync(DogfoodScene scene, PolicyDecision decision)
    {
        if (decision.Target != "node demo.js")
        {
            await scene.Journal.NoteAsync($"NOT OFFERED: job 1 asked '{decision.Target}', not exactly node demo.js; denying it");
            _ = await scene.Actions.AnswerPermissionAsync(decision, false, "Run exactly `node demo.js`, nothing chained.", DogfoodRemember.Once);

            return true;
        }

        return await scene.Actions.AnswerPermissionAsync(decision, true, "Always in this repository", DogfoodRemember.InRepository);
    }
}
