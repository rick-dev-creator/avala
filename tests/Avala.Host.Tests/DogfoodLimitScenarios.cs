using System.Globalization;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using static Avala.Host.Tests.DogfoodChecks;
using static Avala.Host.Tests.DogfoodSettings;

namespace Avala.Host.Tests;

internal static class DogfoodLimitScenarios
{
    private const string RehearsedChecks = """
        { "checks": [ { "name": "calculator", "command": "git", "arguments": ["grep", "--quiet", "--fixed-strings", "add(2, 2) = 4", "--", "calculator.txt"], "timeoutSeconds": 60 } ] }
        """;

    public static DogfoodScenario HeldNearLimit { get; } = new("s6", "A job near its usage window is held mid-turn, waits for the reset, and keeps its hold reason across a restart", "Budgets' holdAtLimit, Handoffs' hold rule with JobWaitsForReset, the persisted hold reason, the limit readings on New job", 0.05m, TimeSpan.FromMinutes(15))
    {
        Thresholded = true,
        Seed = setup => new DogfoodSeed
        {
            Committed =
            [
                new(".avala/budget.json", $$"""{ "costPerJob": { "USD": {{DogfoodRepository.Money(setup.Cap)}} }, "holdAtLimit": {{Ratio(setup.Threshold)}} }"""),
                new(".avala/jobs.json", $$"""{ "connection": "{{Main}}", "approval": "keep", "limits": { "onLimit": "hold", "threshold": {{Ratio(setup.Threshold)}} } }"""),
            ],
            Recordings = [new DogfoodScript("s6").Turn().Say("Looking at calc.js first.").Bill(0.0040m, 0.5, "5h", DateTimeOffset.UtcNow.AddHours(1)).Interrupt()],
        },
        Steps = async scene =>
        {
            var job = await scene.SubmitAsync(scene.Tagged("[replay: s6]", DogfoodJobScenarios.Task), Main, "s6");
            await scene.UntilAsync<JobHeld>(found => found.Hold.Job == job, "the hold near the limit");
            await scene.UntilAsync<JobWaitsForReset>(found => found.Wait.Job == job, "the wait for the reset");
            await scene.Actions.RowAsync(job, "row:before", "Waiting");
            await scene.Actions.PillAsync(job, "pill:before");
            await AttemptsAsync(scene, job);
            await scene.RestartAsync();
            await scene.Actions.RowAsync(job, "row:after", "Waiting");
            await scene.Actions.PillAsync(job, "pill:after");
            scene.Evidence.Note("wait:after", scene.App.Get<IHandoffs>().WaitOf(job).Match(wait => $"{wait.Connection.Value} {wait.Window} {wait.ResumesAt.Match(at => at.ToString("O", CultureInfo.InvariantCulture), () => "none")}", () => string.Empty));
            await scene.Actions.InspectAsync(job, "after-restart");
            await DiscardAsync(scene, job);
        },
        Checks =
        [
            OfJob("The job is held LimitNearlyReached mid-turn, its attempt Interrupted", "s6", (evidence, job) =>
                DogfoodVerdict.Unless(evidence.First<JobHeld>(found => found.Hold.Job == job), "no hold", found =>
                    Expect(evidence, found.Hold.Reason == HoldReason.LimitNearlyReached && evidence.Fact("attempts").Contains("Interrupted", StringComparison.Ordinal), $"held {found.Hold.Reason}, halt {found.Hold.Halt}, attempts [{evidence.Fact("attempts")}]"))),
            OfJob("JobWaitsForReset names claude, the window and its reset time", "s6", (evidence, job) =>
                DogfoodVerdict.Unless(evidence.First<JobWaitsForReset>(found => found.Wait.Job == job), "no wait", found =>
                    Expect(evidence, found.Wait.Connection.Value == Main && found.Wait.Window.Length > 0 && found.Wait.ResumesAt.IsSome, $"waits on {found.Wait.Connection.Value} for the {found.Wait.Window} window until {found.Wait.ResumesAt.Match(at => at.ToString("O", CultureInfo.InvariantCulture), () => "never")}"))),
            Fact("The pill reads Held · … until the window resets", "pill:before", text => text.StartsWith("Held", StringComparison.Ordinal)),
            new("After a restart the sidebar and the pill still say the job waits for its window, never retries ran out", evidence =>
                evidence.Has("row:after")
                    ? Expect(evidence, evidence.Fact("row:after") == evidence.Fact("row:before") && evidence.Fact("pill:after") == evidence.Fact("pill:before") && evidence.Fact("pill:after").StartsWith("Held", StringComparison.Ordinal) && !evidence.Fact("row:after").Contains("retries ran out", StringComparison.Ordinal), $"row '{evidence.Fact("row:before")}' → '{evidence.Fact("row:after")}', pill '{evidence.Fact("pill:before")}' → '{evidence.Fact("pill:after")}'")
                    : DogfoodVerdict.NotReached($"no restart ({evidence.Outcome})")),
            new("The wait is restored from handoffs.db after the restart", evidence =>
                evidence.Has("wait:after")
                    ? Expect(evidence, evidence.Fact("wait:after").StartsWith(Main, StringComparison.Ordinal), $"restored wait '{evidence.Fact("wait:after")}'")
                    : DogfoodVerdict.NotReached($"no restart ({evidence.Outcome})")),
        ],
    };

    public static DogfoodScenario HandedOff { get; } = new("s7", "A job started on an exhausted account is held for its limit, never failed, and handed off on its own to the other account, where it passes its checks", "the usage limit of a really exhausted account, TurnFinished LimitReached, the limit hold, Handoffs when held, the brief, spend per connection", 0.25m, TimeSpan.FromMinutes(15))
    {
        UsesExhausted = true,
        Seed = setup => new DogfoodSeed
        {
            Committed =
            [
                new(".avala/jobs.json", $$"""{ "connection": "{{Main}}", "approval": "keep", "limits": { "onLimit": "handoff-same-harness", "connections": ["{{Exhausted}}", "{{Main}}"] } }"""),
                new(".avala/checks.json", setup.Rehearsal ? RehearsedChecks : DogfoodRepository.Checks),
            ],
        },
        Steps = async scene =>
        {
            var job = await scene.SubmitAsync(scene.Tagged("[simulate: account-exhausted]", DogfoodJobScenarios.Task), Exhausted, "s7");
            var held = await scene.UntilAsync<JobHeld>(found => found.Hold.Job == job, "the hold for the limit of the exhausted account");
            scene.Evidence.Note("held", $"{held.Hold.Reason} {held.Hold.Halt} {Reading(held.Hold.Limit)}");
            var handed = await scene.UntilAsync<JobHandedOff>(found => found.Job == job, "the handoff");
            var mark = scene.Mark;
            await scene.SettledAsync(job, "the job to settle on the other account", mark);
            await scene.Actions.ConversationAsync(job, "conversation:s7");
            await scene.Actions.InspectAsync(job, "handed-off");
            scene.Evidence.Note("handoff", $"{handed.From.Value} → {handed.To.Value}");
        },
        Checks =
        [
            OfJob("The first turn runs on claude-work and ends at its usage limit, not failed: TurnFinished LimitReached with a window at 100%", "s7", (evidence, job) =>
                DogfoodVerdict.Unless(FirstTurn(evidence, job), $"no turn finished on {Exhausted} ({evidence.Outcome})", found =>
                    Expect(evidence, found.Outcome == TurnOutcome.LimitReached && found.Limit.Match(limit => limit.UsedFraction >= 1, () => false), $"{found.Outcome} {Reading(found.Limit)}"))),
            OfJob("Avala holds the job for its limit with that reading and never fails it", "s7", (evidence, job) =>
                DogfoodVerdict.Unless(evidence.First<JobHeld>(found => found.Hold.Job == job), $"no hold ({evidence.Outcome})", found =>
                {
                    var turn = FirstTurn(evidence, job).Match(finished => Reading(finished.Limit), () => "no turn");

                    return Expect(evidence, found.Hold.Reason == HoldReason.LimitNearlyReached && Reading(found.Hold.Limit) == turn && !evidence.Journey(job).Contains(JobStatus.Failed), $"held {found.Hold.Reason} at {Reading(found.Hold.Limit)} after a turn at {turn}; journey [{string.Join(" → ", evidence.Journey(job))}]");
                })),
            OfJob("No work is billed on claude-work", "s7", (evidence, job) =>
            {
                var spent = evidence.SpentBy(evidence.ConnectionOf);
                var recorded = evidence.First<HandoffRecorded>(found => found.Handoff.Job == job).Match(found => found.Handoff.Spent.Sum(cost => cost.Amount), () => 0m);

                return Expect(evidence, evidence.SessionsOf(job).Any(opened => opened.Connection.Value == Exhausted) && spent.GetValueOrDefault(Exhausted) == 0m && recorded == 0m, $"per connection [{string.Join(", ", spent.Select(pair => $"{pair.Key} {pair.Value}"))}], recorded at the handoff {recorded}");
            }),
            OfJob("Avala hands the job off on its own from claude-work to claude, comparing claude-work at its limit", "s7", (evidence, job) =>
                DogfoodVerdict.Unless(evidence.First<JobHandedOff>(found => found.Job == job), $"no handoff ({evidence.Outcome})", found =>
                    Expect(evidence, found.From.Value == Exhausted && found.To.Value == Main && found.Choice.Compared.Any(candidate => candidate.Connection.Value == Exhausted && candidate.Used >= 1 && !candidate.Available), $"{found.From.Value} → {found.To.Value}, compared [{string.Join(", ", found.Choice.Compared.Select(candidate => string.Create(CultureInfo.InvariantCulture, $"{candidate.Connection.Value} {candidate.Used:P0} of {candidate.Threshold:P0} available {candidate.Available}")))}]"))),
            OfJob("The new session runs on claude, a different account than claude-work", "s7", (evidence, job) =>
            {
                var sessions = evidence.SessionsOf(job).ToList();
                var accounts = sessions.Select(opened => opened.Account.Match(account => account.Id, () => "none")).Distinct().Count();

                return Expect(evidence, sessions.Any(opened => opened.Connection.Value == Main) && accounts > 1, $"sessions [{string.Join(", ", sessions.Select(opened => $"{opened.Connection.Value}={opened.Account.Match(account => account.Id, () => "none")}").Distinct())}]");
            }),
            Fact("The new session's first message is Avala's brief naming claude-work and its window", "conversation:s7", text => text.Contains($"handed off to you from {Exhausted}, which reached", StringComparison.Ordinal)),
            OfJob("The job then passes its checks on claude and reaches AwaitingReview", "s7", (evidence, job) =>
            {
                var passed = evidence.All<AttemptVerified>(found => found.Report.Job == job).Select(found => found.Report).LastOrDefault();

                return Expect(evidence, evidence.Journey(job).Contains(JobStatus.AwaitingReview) && passed?.Outcome == VerificationOutcome.Passed, $"journey [{string.Join(" → ", evidence.Journey(job))}], last verification {passed?.Outcome.ToString() ?? "none"}");
            }),
            Fact("The conversation tells the person the first turn ran out of its usage window", "conversation:s7", text => text.Contains("Ran out of its usage window", StringComparison.Ordinal)),
            OfJob("The inspector shows the hold's reading: Handed off from claude-work to claude at the held percentage", "s7", (evidence, job) =>
            {
                var percent = evidence.First<JobHeld>(found => found.Hold.Job == job).Match(found => found.Hold.Limit.Match(limit => Percent(limit.UsedFraction), () => "no reading"), () => "no hold");
                var lines = evidence.FactsOf("inspector:handed-off:").Where(text => text.Contains("Handed off", StringComparison.Ordinal)).ToList();

                return Expect(evidence, lines.Any(text => text.Contains($"Handed off from {Exhausted} to {Main} at {percent} of the", StringComparison.Ordinal)), $"held at {percent}; {Short(string.Join(" / ", lines))}");
            }),
        ],
    };

    public static string Ratio(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Percent(double fraction) => fraction.ToString("P0", CultureInfo.InvariantCulture).Replace(" ", string.Empty, StringComparison.Ordinal);

    private static string Reading(Option<UsageLimit> limit) =>
        limit.Match(found => $"{found.Window} {Percent(found.UsedFraction)}", () => "no reading");

    private static Option<TurnFinished> FirstTurn(DogfoodEvidence evidence, JobId job) =>
        evidence.First<TurnFinished>(found => evidence.JobOf(found.Session) == Option<JobId>.Some(job) && evidence.ConnectionOf(found.Session) == Exhausted);

    private static async Task AttemptsAsync(DogfoodScene scene, JobId job)
    {
        var history = await scene.App.Get<IJobCatalog>().HistoryAsync(job, Cancellation);
        scene.Evidence.Note("attempts", history.Match(found => string.Join(", ", found.Attempts.Select(attempt => $"{attempt.Origin} {attempt.Outcome}")), () => string.Empty));
    }

    private static async Task DiscardAsync(DogfoodScene scene, JobId job)
    {
        try
        {
            await scene.Actions.DiscardAsync(job, "s6");
        }
        catch (TimeoutException)
        {
            await scene.Journal.NoteAsync("The held job could not be discarded from its review sheet; the stop of the application ends its session");
        }
    }
}
