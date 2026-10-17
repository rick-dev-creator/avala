using System.Text.Json.Nodes;
using Avala.Autopilot.Contracts;
using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Triggers.Contracts;
using static Avala.Host.Tests.DogfoodChecks;
using static Avala.Host.Tests.DogfoodRepository;
using static Avala.Host.Tests.DogfoodSettings;

namespace Avala.Host.Tests;

internal static class DogfoodAutomationScenarios
{
    private const string Pushed = "Avala pushes your work to the pull request once your turn passes its checks.";

    public static DogfoodScenario Webhook { get; } = new("s8", "A signed webhook starts a job on its own, capped at the repository's autonomy; bad deliveries start nothing", "Triggers: the loopback webhook, HMAC checks, replay guard, autonomy ceiling, run audit, the Triggers page", 0.10m, TimeSpan.FromMinutes(15))
    {
        Seed = setup => new DogfoodSeed
        {
            Data =
            [
                new("triggers.json", $$"""
                    {
                      "repositories": [{{Json(setup.Place.Repository)}}],
                      "triggers": [
                        {
                          "id": "greet",
                          "repository": {{Json(setup.Place.Repository)}},
                          "webhook": { "secretEnv": "{{HookSecret}}", "ratePerHour": 10 },
                          "instruction": {{Json(setup.Tagged("[replay: s8]", "Create GREETING.md that greets {{payload.name}} in one line."))}},
                          "autonomy": "autonomous"
                        }
                      ]
                    }
                    """),
            ],
            Recordings = [new DogfoodScript("s8").Turn().Write("GREETING.md", "Hello, Ana!\n").Say("Created GREETING.md greeting Ana.").Bill(0.0062m, 0.22).Finish()],
        },
        Steps = async scene =>
        {
            await scene.UntilAsync<StartupCompleted>(_ => true, "the startup");
            var automation = Automation(scene);
            var secret = Environment.GetEnvironmentVariable(HookSecret)!;
            const string body = """{ "name": "Ana" }""";
            await automation.PostAsync("greet", body, DogfoodSigning.Unsigned, "unsigned");
            await automation.PostAsync("greet", body, new DogfoodSigning("not the secret", "n-forged"), "forged");
            await automation.PostAsync("greet", body, new DogfoodSigning(secret, "n-1"), "signed");
            await automation.PostAsync("greet", body, new DogfoodSigning(secret, "n-1"), "replayed");
            var fired = await scene.UntilAsync<TriggerFired>(_ => true, "the trigger to fire");
            var job = fired.Run.Job.Match(found => found, () => throw new DogfoodStoppedException("The trigger fired without a job"));
            scene.Evidence.Note("submitted:s8", job.ToString());
            await scene.SettledAsync(job, "the triggered job to settle");
            await scene.ReadAsync("greeting", Path.Combine(scene.WorktreeOf(job), "GREETING.md"));
            await scene.UntilAsync<WebhookReceived>(found => found.Delivery.Verdict == DeliveryVerdict.Replayed, "the replayed delivery");
            await automation.TriggersAsync("after");
            scene.Actions.ShowJobs();
        },
        Checks =
        [
            new("The four requests get 401 NotSigned, 401 BadSignature, 202 Accepted and 409 Replayed", evidence =>
            {
                string[] statuses = [.. ((string[])["unsigned", "forged", "signed", "replayed"]).Select(label => evidence.Fact($"webhook:{label}"))];
                string[] verdicts = [.. evidence.All<WebhookReceived>().Select(found => found.Delivery.Verdict.ToString())];

                return Expect(evidence, statuses.SequenceEqual(["401 Unauthorized", "401 Unauthorized", "202 Accepted", "409 Conflict"]) && verdicts.SequenceEqual(["NotSigned", "BadSignature", "Accepted", "Replayed"]), $"statuses [{string.Join(", ", statuses)}], deliveries [{string.Join(", ", verdicts)}]");
            }),
            Fact("Each delivery is listed on the Triggers page", "triggers:after", text => DogfoodChecks.Between(text, "Deliveries: ", Environment.NewLine).Split(" | ", StringSplitOptions.RemoveEmptyEntries).Length >= 4),
            new("Exactly one TriggerFired is published: Submitted, asked autonomous, applied supervised, autonomy capped", evidence =>
            {
                var fired = evidence.All<TriggerFired>().Select(found => found.Run).ToList();

                return Expect(evidence, fired.Count == 1 && fired[0].Outcome == RunOutcome.Submitted && fired[0].Asked == Autonomy.Autonomous && fired[0].Applied == Autonomy.Supervised && fired[0].AutonomyCapped, string.Join("; ", fired.Select(run => $"{run.Outcome} asked {run.Asked} applied {run.Applied} capped {run.AutonomyCapped}")));
            }),
            OfJob("The job writes GREETING.md greeting Ana and reaches review", "s8", (evidence, job) =>
                Expect(evidence, evidence.Fact("greeting").Contains("Ana", StringComparison.Ordinal) && evidence.Journey(job).Contains(JobStatus.AwaitingReview), $"GREETING.md '{evidence.Fact("greeting").Trim()}', journey [{string.Join(" → ", evidence.Journey(job))}]")),
        ],
    };

    public static DogfoodScenario Loop { get; } = new("s9", "A loop runs under the machine's declared limits, approves clean evidence on its own, and survives a restart", "Autopilot loops, autopilot.json limits, approve cleanEvidence, the backlog, the Triggers page loop section, loop restore", 0.15m, TimeSpan.FromMinutes(15))
    {
        Seed = setup => new DogfoodSeed
        {
            Committed =
            [
                new(".avala/jobs.json", $$"""{ "connection": "{{Main}}", "approval": "keep", "autopilot": { "approve": "cleanEvidence" } }"""),
                new(".avala/backlog.json", $$"""{ "tasks": [ { "id": "div", "instruction": {{Json(setup.Tagged("[replay: s9]", "Add `div(a, b)` to calc.js with a node:test test in test/calc.test.js. Read and change files with your file tools, and run no shell command other than `node --test`."))}} } ] }"""),
            ],
            Data =
            [
                new("autopilot.json", """{ "iterations": 1 }"""),
                new("triggers.json", $$"""
                    {
                      "repositories": [{{Json(setup.Place.Repository)}}],
                      "triggers": [
                        {
                          "id": "nightly",
                          "repository": {{Json(setup.Place.Repository)}},
                          "schedule": { "everyMinutes": 43200 },
                          "target": "loop",
                          "instruction": {{Json(setup.Tagged("[replay: s9]", "Add `mul(a, b)` to calc.js with a node:test test in test/calc.test.js. Read and change files with your file tools, and run no shell command other than `node --test`."))}}
                        }
                      ]
                    }
                    """),
            ],
            Recordings = [Calculator("s9")],
        },
        Steps = async scene =>
        {
            await scene.UntilAsync<StartupCompleted>(_ => true, "the startup");
            var automation = Automation(scene);
            await automation.RunNowAsync("nightly", "s9");
            var ended = await scene.UntilAsync<LoopEnded>(_ => true, "the loop to end");
            await automation.LoopsAsync("ended", loops => loops["Loops"].Items.Any(loop => loop["Status"].Text.StartsWith("Ended", StringComparison.Ordinal)));
            await scene.RestartAsync();
            var restarted = scene.Mark;
            await scene.UntilAsync<StartupCompleted>(_ => true, "the startup after the restart", restarted);
            scene.Evidence.Note("loop:ended", Line(ended.State));
            scene.Evidence.Note("loops:service", string.Join(Environment.NewLine, scene.App.Get<IAutopilot>().Loops().Select(Line)));
            await automation.LoopsAsync("restarted", loops => loops["Loops"].Items.Any(loop => loop["Status"].Text.StartsWith("Ended", StringComparison.Ordinal)));

            if (scene.Evidence.Seen.Skip(restarted).Any(seen => seen.Event is LoopStarted))
            {
                scene.Evidence.Find("After the restart a new loop started on its own from the trigger's task that the ended loop left queued, so the machine's iteration limit does not bound the trigger's work across a restart");
                await scene.UntilAsync<LoopEnded>(found => found.State.Loop != ended.State.Loop, "the loop started after the restart to end", restarted);
            }
        },
        Checks =
        [
            new("LoopStarted is published", evidence => Expect(evidence, evidence.All<LoopStarted>().Any(), $"{evidence.All<LoopStarted>().Count()} loops started")),
            new("One task becomes one job that passes its checks and is approved automatically, with its evidence in the digest", evidence =>
                DogfoodVerdict.Unless(FirstLoop(evidence), "no loop started", loop =>
                {
                    var taken = evidence.All<LoopTaskTaken>(found => found.Loop == loop).ToList();
                    var iterations = evidence.All<LoopIterated>(found => found.Loop == loop).Select(found => found.Iteration).ToList();

                    return Expect(evidence, taken.Count == 1 && iterations.Count == 1 && iterations[0].Outcome == IterationOutcome.ApprovedAutomatically && evidence.Journey(taken[0].Job).Contains(JobStatus.Approved) && evidence.All<AutoApprovalDecided>(found => found.Decision.Job == taken[0].Job && found.Decision.Approved).Any(), $"taken [{string.Join(", ", taken.Select(task => $"{task.Task.Source} {task.Task.Key}"))}], iterations [{string.Join(", ", iterations.Select(iteration => $"#{iteration.Number} {iteration.Outcome}"))}]");
                })),
            new("BreakerTripped Iterations is published, then LoopEnded", evidence =>
                DogfoodVerdict.Unless(FirstLoop(evidence), "no loop started", loop =>
                {
                    var order = evidence.Seen.Select(seen => seen.Event).Where(found => found is BreakerTripped { Loop: var tripped } && tripped == loop || found is LoopEnded { State.Loop: var over } && over == loop).Select(found => found is BreakerTripped tripped ? $"BreakerTripped {tripped.Trip.Breaker}" : "LoopEnded").ToList();

                    return Expect(evidence, order.SequenceEqual(["BreakerTripped Iterations", "LoopEnded"]), $"[{string.Join(", ", order)}]");
                })),
            new("The loop never takes the second task", evidence =>
                DogfoodVerdict.Unless(FirstLoop(evidence), "no loop started", loop => Expect(evidence, evidence.All<LoopTaskTaken>(found => found.Loop == loop).Count() == 1 && evidence.All<LoopEnded>(found => found.State.Loop == loop).Any(), $"{evidence.All<LoopTaskTaken>(found => found.Loop == loop).Count()} tasks taken by the loop"))),
            Fact("The Triggers page reads Ended · breaker tripped and 1 approved on their own · spent … USD at API prices", "loops:ended", text => text.Contains("Ended · breaker tripped: iterations", StringComparison.Ordinal) && text.Contains("1 approved on their own", StringComparison.Ordinal) && text.Contains("at API prices", StringComparison.Ordinal)),
            new("After a restart Loops() and the page show the same loop and digest", evidence =>
                evidence.Has("loops:restarted")
                    ? Expect(evidence, evidence.Fact("loops:service").Split(Environment.NewLine).Contains(evidence.Fact("loop:ended")) && evidence.Fact("loops:restarted").Split(Environment.NewLine).Contains(evidence.Fact("loops:ended").Split(Environment.NewLine)[0]), $"service [{evidence.Fact("loops:service").ReplaceLineEndings("; ")}] holds [{evidence.Fact("loop:ended")}]; page [{evidence.Fact("loops:restarted").ReplaceLineEndings("; ")}]")
                    : DogfoodVerdict.NotReached($"no restart ({evidence.Outcome})")),
        ],
    };

    private static Option<LoopId> FirstLoop(DogfoodEvidence evidence) => evidence.First<LoopStarted>(_ => true).Map(found => found.State.Loop);

    private static string Line(LoopState state) => $"{state.Loop.Value} {state.Status} {state.Iterations} {state.Ending.Match(ending => ending.ToString(), () => "none")}";

    public static DogfoodScenario PullRequest { get; } = new("s10", "An approved job's pull request is watched, the agent is woken by failing CI and fixes it", "Forges: the pull-request approval, a push to a local remote, the watch, wake-ups with feedback, the inspector Pull request section", 0.20m, TimeSpan.FromMinutes(15))
    {
        Seed = _ => new DogfoodSeed
        {
            Origin = true,
            Committed = [new(".avala/jobs.json", $$"""{ "connection": "{{Main}}", "approval": "pull-request", "pullRequest": { "forge": "local", "onPullRequest": "wake-on-ci", "maxWakeUps": 1, "redeliver": "automatic" } }""")],
            Data = [new("forges.json", """{ "pollSeconds": 15, "forges": [ { "name": "local", "forge": "simulated" } ] }""")],
        },
        Steps = async scene =>
        {
            var automation = Automation(scene);
            var job = await scene.SubmitAsync(scene.Tagged("[simulate: fix-after-feedback]", "[forge: ci-fails-once] Add `sub(a, b)` to calc.js with a test."), Main, "s10");

            if (await scene.SettledAsync(job, "the job to settle") != JobStatus.AwaitingReview)
            {
                return;
            }

            await scene.Actions.ApproveAsync(job, "s10");
            await scene.UntilAsync<PullRequestOpened>(found => found.Job == job, "the pull request");
            var mark = scene.Mark;
            await automation.PullRequestAsync(job, "opened", checkNow: true);
            await scene.UntilAsync<PullRequestWakeUp>(found => found.WakeUp.Job == job, "the wake-up");
            await scene.UntilAsync<PullRequestOpened>(found => found.Job == job, "the fix pushed to the pull request", mark);
            await automation.PullRequestAsync(job, "redelivered", checkNow: true);
            await scene.UntilAsync<PullRequestWatchChanged>(found => found.State.Job == job && found.State.Status == WatchStatus.Ended, "the watch to end");
            await automation.PullRequestAsync(job, "green", checkNow: false, status: "Checks passed");
            await scene.ReadAsync("forge", Path.Combine(scene.Place.Origin, "avala-forge.json"));
        },
        Checks =
        [
            OfJob("PullRequestOpened is published for the job's branch on origin", "s10", (evidence, job) =>
                DogfoodVerdict.Unless(evidence.First<PullRequestOpened>(found => found.Job == job), $"no pull request ({evidence.Outcome})", found =>
                    DogfoodVerdict.Pass($"#{found.PullRequest.Number} {found.PullRequest.Head} → {found.PullRequest.Base} on {found.Forge.Value}"))),
            OfJob("One PullRequestWakeUp (ChecksFailed) continues the agent with the failing check as feedback", "s10", (evidence, job) =>
            {
                var wakeUps = evidence.All<PullRequestWakeUp>(found => found.WakeUp.Job == job).Select(found => found.WakeUp).ToList();

                return Expect(evidence, wakeUps.Count == 1 && wakeUps[0].Reason == WakeReason.ChecksFailed && wakeUps[0].Outcome == WakeOutcome.Woken && wakeUps[0].Feedback.Contains("build", StringComparison.Ordinal) && wakeUps[0].Feedback.TrimEnd().EndsWith(Pushed, StringComparison.Ordinal), $"wake-ups [{string.Join("; ", wakeUps.Select(wakeUp => $"{wakeUp.Reason} {wakeUp.Outcome}: {Short(wakeUp.Feedback)}"))}]");
            }),
            OfJob("The agent's next turn passes the gates and a new head is pushed to the same pull request", "s10", (evidence, job) =>
            {
                var opened = evidence.All<PullRequestOpened>(found => found.Job == job).ToList();
                var heads = Heads(evidence.Fact("forge"));

                return Expect(evidence, opened.Count >= 2 && opened.Select(found => found.PullRequest.Number).Distinct().Count() == 1 && heads >= 2, $"{opened.Count} deliveries to #{string.Join(", #", opened.Select(found => found.PullRequest.Number).Distinct())}, {heads} heads in avala-forge.json");
            }),
            OfJob("The next poll finds the pull request green and keeps watching it until it is merged or closed", "s10", (evidence, job) =>
                DogfoodVerdict.Unless(evidence.First<PullRequestWatchChanged>(found => found.State.Job == job && found.State.Status == WatchStatus.Green), "the watch never went green", found =>
                    Expect(evidence, found.State.Ended.IsNone, $"green after {found.State.WakeUps} wake-ups"))),
            Fact("The inspector reads Checks passed · waiting for it to be merged, with one wake-up Woken", "pull-request:green", text => text.Contains("Checks passed · waiting for it to be merged", StringComparison.Ordinal) && DogfoodChecks.Between(text, "WakeUps=[", "]").Length > 0),
        ],
    };

    private static DogfoodAutomation Automation(DogfoodScene scene) => new(scene.App, scene.Actions, scene.App.Camera, scene.Evidence);

    private static int Heads(string forge)
    {
        try
        {
            return JsonNode.Parse(forge) is { } state ? Count(state) : 0;
        }
        catch (System.Text.Json.JsonException)
        {
            return 0;
        }
    }

    private static int Count(JsonNode node) => node switch
    {
        JsonObject found => found.Select(pair => string.Equals(pair.Key, "heads", StringComparison.OrdinalIgnoreCase) && pair.Value is JsonArray heads ? heads.Count : pair.Value is null ? 0 : Count(pair.Value)).DefaultIfEmpty(0).Max(),
        JsonArray items => items.Select(item => item is null ? 0 : Count(item)).DefaultIfEmpty(0).Max(),
        _ => 0,
    };
}
