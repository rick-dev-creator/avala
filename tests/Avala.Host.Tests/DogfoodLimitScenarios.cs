using System.Globalization;
using Avala.Agents.Contracts;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using static Avala.Host.Tests.DogfoodChecks;
using static Avala.Host.Tests.DogfoodSettings;

namespace Avala.Host.Tests;

internal static class DogfoodLimitScenarios
{
    private const string RehearsedChecks = """
        { "checks": [ { "name": "calculator", "command": "git", "arguments": ["grep", "--quiet", "--fixed-strings", "add(2, 2) = 4", "--", "calculator.txt"], "timeoutSeconds": 60 } ] }
        """;

    private const string ProbedChecks = """
        {
          "checks": [
            { "name": "tests", "command": "node", "arguments": ["--test"], "timeoutSeconds": 60 },
            { "name": "changelog", "command": "test", "arguments": ["-f", "CHANGELOG.md"], "timeoutSeconds": 10 }
          ]
        }
        """;

    public static DogfoodScenario HeldNearLimit { get; } = new("s6", "A job near its usage window is held mid-turn, waits for the reset, and keeps its hold reason across a restart", "Budgets' holdAtLimit, Handoffs' hold rule with JobWaitsForReset, the persisted hold reason, the limit readings on New job", 0.05m, TimeSpan.FromMinutes(15))
    {
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
            OfJob("JobWaitsForReset names claude-work, the window and its reset time", "s6", (evidence, job) =>
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

    public static DogfoodScenario HandedOff { get; } = new("s7", "A job at its limit on one real account is handed off to the other with Avala's brief", "Handoffs at the end of a turn, the brief from Avala's records, JobHandedOff and HandoffRecorded, spend split per connection, two real accounts", 0.25m, TimeSpan.FromMinutes(15))
    {
        UsesOther = true,
        Seed = setup => new DogfoodSeed
        {
            Committed =
            [
                new(".avala/jobs.json", $$"""{ "connection": "{{Main}}", "approval": "keep", "limits": { "onLimit": "handoff-same-harness", "threshold": {{Ratio(setup.Threshold)}}, "connections": ["{{Main}}", "{{Other}}"] } }"""),
                new(".avala/checks.json", setup.Rehearsal ? RehearsedChecks : ProbedChecks),
            ],
        },
        Steps = async scene =>
        {
            var job = await scene.SubmitAsync(scene.Tagged("[simulate: limit-handoff]", DogfoodJobScenarios.Task), Main, "s7");
            var mark = scene.Mark;
            var handed = await scene.UntilAsync<JobHandedOff>(found => found.Job == job, "the handoff");
            await scene.SettledAsync(job, "the job to settle on the other account", mark);
            await scene.Actions.ConversationAsync(job, "conversation:s7");
            await scene.Actions.InspectAsync(job, "handed-off");
            scene.Evidence.Note("handoff", $"{handed.From.Value} → {handed.To.Value}");
        },
        Checks =
        [
            OfJob("JobHandedOff goes from claude-work to claude with a choice that compares claude-work and its reading", "s7", (evidence, job) =>
                DogfoodVerdict.Unless(evidence.First<JobHandedOff>(found => found.Job == job), $"no handoff: the first attempt passed its checks or the reading stayed under the threshold ({evidence.Outcome})", found =>
                    Expect(evidence, found.From.Value == Main && found.To.Value == Other && found.Choice.Compared.Any(candidate => candidate.Connection.Value == Main), $"{found.From.Value} → {found.To.Value}, compared [{string.Join(", ", found.Choice.Compared.Select(candidate => string.Create(CultureInfo.InvariantCulture, $"{candidate.Connection.Value} {candidate.Used:P0} of {candidate.Threshold:P0} available {candidate.Available}")))}]"))),
            OfJob("The new session runs on a different account than the first", "s7", (evidence, job) =>
            {
                var accounts = evidence.SessionsOf(job).Select(opened => $"{opened.Connection.Value}={opened.Account.Match(account => account.Id, () => "none")}").Distinct().ToList();

                return Expect(evidence, evidence.SessionsOf(job).Select(opened => opened.Account.Match(account => account.Id, () => "none")).Distinct().Count() > 1, $"sessions [{string.Join(", ", accounts)}]");
            }),
            Fact("The new session's first message is Avala's brief naming claude-work and its window", "conversation:s7", text => text.Contains($"handed off to you from {Main}, which reached", StringComparison.Ordinal)),
            OfJob("The job then passes its checks on the other account and reaches AwaitingReview", "s7", (evidence, job) =>
            {
                var passed = evidence.All<Avala.Verification.Contracts.AttemptVerified>(found => found.Report.Job == job).Select(found => found.Report).LastOrDefault();

                return Expect(evidence, evidence.Journey(job).Contains(JobStatus.AwaitingReview) && passed?.Outcome == Avala.Verification.Contracts.VerificationOutcome.Passed, $"journey [{string.Join(" → ", evidence.Journey(job))}], last verification {passed?.Outcome.ToString() ?? "none"}");
            }),
            OfJob("HandoffRecorded splits the spend between the two connections", "s7", (evidence, job) =>
                DogfoodVerdict.Unless(evidence.First<HandoffRecorded>(found => found.Handoff.Job == job), "no handoff recorded", found =>
                    Expect(evidence, found.Handoff.Spent.Count > 0 && evidence.SpentBy(evidence.ConnectionOf).Count > 1, $"spent on {found.Handoff.From.Value} before the handoff: {string.Join(", ", found.Handoff.Spent.Select(cost => $"{cost.Amount} {cost.Currency}"))}; per connection [{string.Join(", ", evidence.SpentBy(evidence.ConnectionOf).Select(pair => $"{pair.Key} {pair.Value}"))}]"))),
            new("The inspector lists Handed off from claude-work to claude at N% of the … window", evidence =>
                Expect(evidence, evidence.FactsOf("inspector:handed-off:").Any(text => text.Contains($"Handed off from {Main} to {Other} at", StringComparison.Ordinal)), Short(string.Join(" / ", evidence.FactsOf("inspector:handed-off:").Where(text => text.Contains("Handed off", StringComparison.Ordinal)))))),
        ],
    };

    public static string Ratio(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

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
