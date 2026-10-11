using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Budgets.Contracts;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using static Avala.Host.Tests.DogfoodChecks;
using static Avala.Host.Tests.DogfoodSettings;

namespace Avala.Host.Tests;

internal static class DogfoodTeamScenarios
{
    private const string Orchestration =
        "Work as an orchestrator, in this order. "
        + "1) Delegate to one sub-agent with `autonomy: \"supervised\"`: implement `mul(a, b)` in calc.js with a `node:test` test in test/mul.test.js, and run `node demo.js` once to show the result. "
        + "2) While you wait, if the `delegate` call returns with outcome `asking`, decide it with `answer_child` (allow ordinary development actions inside the repository), then call `wait_child`. "
        + "3) After it is integrated, run `node --test`. "
        + "4) Then delegate one review with `role: \"reviewer\"`: it reviews calc.js and its tests and writes REVIEW.md (or reports its findings in its final message if it cannot write). "
        + "5) Finish with a short summary. Do not start servers, install anything or use the network.";

    private const string SlowChecks = """{ "checks": [ { "name": "slow suite", "command": "bash", "arguments": ["-c", "sleep 21; node --test"], "timeoutSeconds": 120 } ] }""";

    public static DogfoodScenario Orchestrated { get; } = new("s11", "An orchestrator on Sonnet delegates to a Haiku child on the same account, which asks it and gets an early answer; a read-only reviewer reports", "delegation to another connection of the same account, children asking their parent through the pending delegate call, answer_child and wait_child, a read-only reviewer, integration into the parent's worktree", 1.30m, TimeSpan.FromMinutes(40))
    {
        LeadModel = "sonnet",
        Seed = _ => new DogfoodSeed
        {
            Committed =
            [
                new("demo.js", "console.log(require('./calc').add(2, 3));\n"),
                new(".avala/permissions.json", DogfoodRepository.Permissions("autonomous")),
                new(".avala/budget.json", """{ "costPerJob": { "USD": 1.20 }, "carvePerChild": 0.3 }"""),
                new(".avala/jobs.json", $$"""
                    {
                      "connection": "{{Lead}}",
                      "approval": "keep",
                      "delegation": { "connections": ["{{Main}}"], "maxDepth": 1, "maxChildren": 2, "escalation": "parent", "parentWindowSeconds": 180, "role": "worker" }
                    }
                    """),
            ],
        },
        Steps = async scene =>
        {
            var parent = await scene.SubmitAsync(scene.Tagged("[simulate: delegate-asks-parent-then-review]", Orchestration), Lead, "s11");

            if (await scene.SettledAsync(parent, "the orchestrator to settle") != JobStatus.AwaitingReview)
            {
                return;
            }

            var worktree = scene.WorktreeOf(parent);
            await scene.ReadAsync("parent:calc", Path.Combine(worktree, "calc.js"));
            scene.Evidence.Note("parent:review-file", File.Exists(Path.Combine(worktree, "REVIEW.md")) ? "present" : "absent");
            await scene.Actions.InspectAsync(parent, "parent");
            await scene.Actions.ApproveAsync(parent, "s11");
            await scene.StatusAsync(parent, "the approval", 0, JobStatus.Approved);
        },
        Checks =
        [
            new("ParentAsked is published with InCall true", evidence =>
                DogfoodVerdict.Unless(evidence.First<ParentAsked>(_ => true), $"no child asked its parent ({evidence.Outcome})", asked => Expect(evidence, asked.InCall, $"asked '{asked.Asking}', in call {asked.InCall}"))),
            OfJob("The parent's delegate call returns early with outcome asking", "s11", (evidence, parent) =>
                Expect(evidence, evidence.AgentOf(parent).OfType<ToolReturned>().Any(returned => returned.Result.Content.Contains("\"outcome\":\"asking\"", StringComparison.Ordinal)), $"{evidence.AgentOf(parent).OfType<ToolReturned>().Count()} tool results of the parent")),
            OfJob("The parent calls answer_child with allow and the answer names the parent", "s11", (evidence, parent) =>
            {
                var called = evidence.AgentOf(parent).OfType<ToolCalled>().Where(call => call.Tool.EndsWith("answer_child", StringComparison.Ordinal)).ToList();
                var answered = evidence.All<PermissionAnswered>(found => found.Answer.Parent == Option<JobId>.Some(parent)).ToList();

                return Expect(evidence, called.Any(call => call.Input.Contains("allow", StringComparison.Ordinal)) && answered.Count > 0, $"answer_child calls [{string.Join(" | ", called.Select(call => call.Input))}], answers by the parent {answered.Count}");
            }),
            OfJob("The child's audit holds the answer given by its parent", "s11", (evidence, parent) =>
            {
                var children = Children(evidence, parent);

                return Expect(evidence, evidence.All<PermissionAnswered>(found => found.Answer.Parent == Option<JobId>.Some(parent) && found.Answer.Job.Match(children.Contains, () => false) && found.Answer.Answer == PermissionAnswer.Allow).Any(), $"answers of the children [{string.Join(", ", evidence.All<PermissionAnswered>(found => found.Answer.Job.Match(children.Contains, () => false)).Select(found => $"{found.Answer.Target} {found.Answer.Answer} by {evidence.Name(found.Answer.Parent)}"))}]");
            }),
            new("wait_child returns the report: ChildReported Integrated, ReportDelivered through the tool result", evidence =>
            {
                var worker = evidence.All<ChildReported>(found => found.Delegation.Role == ChildRole.Worker).Select(found => found.Delegation).FirstOrDefault();
                var delivered = evidence.All<ReportDelivered>(found => found.Delegation.Role == ChildRole.Worker).Select(found => found.Delegation).FirstOrDefault();

                return worker is null ? DogfoodVerdict.NotReached($"no worker reported ({evidence.Outcome})")
                    : Expect(evidence, worker.Report.Match(report => report.Outcome == ChildOutcome.Integrated, () => false) && delivered is not null && delivered.Answered.Match(answer => answer.Route == AnswerRoute.ToolResult, () => false), $"report {worker.Report.Match(report => report.Outcome.ToString(), () => "none")}, delivered {delivered?.Answered.Match(answer => answer.Route.ToString(), () => "unanswered") ?? "never"}");
            }),
            OfJob("The parent runs on claude-lead and its children on claude, all on the same account", "s11", (evidence, parent) =>
            {
                var parents = evidence.SessionsOf(parent).ToList();
                var children = Children(evidence, parent).SelectMany(evidence.SessionsOf).ToList();
                var accounts = parents.Concat(children).Select(opened => opened.Account.Match(account => account.Id, () => "none")).Distinct().ToList();
                string Of(IEnumerable<SessionOpened> sessions) => string.Join(", ", sessions.Select(opened => $"{opened.Connection.Value}={opened.Account.Match(account => account.Id, () => "none")}").Distinct());

                return Expect(evidence, children.Count > 0 && parents.All(opened => opened.Connection.Value == Lead) && children.All(opened => opened.Connection.Value == Main) && accounts.Count == 1, $"parent [{Of(parents)}], children [{Of(children)}]");
            }),
            OfJob("BudgetCarved is published for each child", "s11", (evidence, parent) =>
            {
                var children = Children(evidence, parent);
                var carved = evidence.All<BudgetCarved>(found => found.Carve.Parent == parent).Select(found => found.Carve.Child).ToHashSet();

                return Expect(evidence, children.Count > 0 && children.All(carved.Contains), $"{children.Count} children, {carved.Count} carves");
            }),
            Fact("The parent's worktree holds the child's mul", "parent:calc", text => text.Contains("mul", StringComparison.Ordinal)),
            new("The reviewer's edit is denied by read-only-denies-edits, it reports, ends Discarded, and leaves no REVIEW.md in the parent's worktree", evidence =>
            {
                var reviewer = evidence.All<ChildReported>(found => found.Delegation.Role == ChildRole.Reviewer).Select(found => found.Delegation).FirstOrDefault();
                var denied = evidence.All<PermissionDecided>(found => found.Decision.Kind == ItemKind.FileEdit && found.Decision.Rule.Match(rule => rule.Name == "read-only-denies-edits", () => false)).Any();

                return reviewer is null ? DogfoodVerdict.NotReached($"no reviewer reported ({evidence.Outcome})")
                    : Expect(evidence, reviewer.Report.Match(report => report.Outcome == ChildOutcome.Reported && report.Status == JobStatus.Discarded, () => false) && evidence.Fact("parent:review-file") == "absent", $"edit denied by the read-only rule {denied}, report {reviewer.Report.Match(report => $"{report.Outcome} {report.Status}", () => "none")}, REVIEW.md in the parent {evidence.Fact("parent:review-file")}");
            }),
            Journey("The parent reaches review and approval keeps it", "s11", JobStatus.AwaitingReview, JobStatus.Approved),
            OfJob("No request of a child is left to a person without a reason", "s11", (evidence, parent) =>
            {
                var children = Children(evidence, parent);
                var passed = evidence.All<PermissionDecided>(found => found.Decision.Delivery == DecisionDelivery.LeftToHuman && found.Decision.Job.Match(children.Contains, () => false)).Select(found => found.Decision).ToList();

                return Expect(evidence, children.Count > 0 && passed.All(decision => decision.Passed.IsSome), $"{passed.Count} child requests went to a person: [{string.Join(", ", passed.Select(decision => $"{decision.Target} ({decision.Passed.Match(reason => reason.ToString(), () => "no reason")})"))}]");
            }),
        ],
    };

    public static DogfoodScenario RestartedWhileChecking { get; } = new("s12", "A restart while the checks run keeps the conversation and leaves no check running", "persistence across an in-process restart in Checking, the recalled conversation, orphaned check processes, late events", 0.30m, TimeSpan.FromMinutes(15))
    {
        Optional = true,
        Seed = _ => new DogfoodSeed
        {
            Committed = [new(".avala/checks.json", SlowChecks)],
            Recordings = [Calculator("s12")],
        },
        Steps = async scene =>
        {
            var job = await scene.SubmitAsync(scene.Tagged("[replay: s12]", DogfoodJobScenarios.Task), Main, "s12");
            await scene.StatusAsync(job, "the checks to start", 0, JobStatus.Checking);
            var before = await scene.Actions.ConversationAsync(job, "conversation:before");
            await scene.RestartAsync();
            scene.Evidence.Note("orphans", string.Join(", ", await DogfoodLeftovers.OrphansAsync()));
            var mark = scene.Mark;
            await scene.Actions.SelectAsync(job);
            var jobs = scene.App.Ui.Jobs;
            await scene.App.Ui.UntilAsync(jobs.Presentation, () => jobs["Conversation"]["Entries"].Items.Any(entry => entry.Kind == "RestartViewModel"), "the restart note in the recalled conversation");
            var after = await scene.Actions.ConversationAsync(job, "conversation:after");
            scene.Evidence.Note("conversation:kept", (before.Count <= after.Count && before.SequenceEqual(after.Take(before.Count), StringComparer.Ordinal)).ToString());

            if (await scene.SettledAsync(job, "the job to settle after the restart", mark) == JobStatus.AwaitingReview)
            {
                await scene.Actions.ApproveAsync(job, "s12");
            }
        },
        Checks =
        [
            Fact("Every conversation entry before the restart reads the same after it", "conversation:kept", text => text == "True"),
            Fact("No check process is left running after the stop", "orphans", text => text.Length == 0),
            Journey("The job is checked again after the restart, reaches review and is approved", "s12", JobStatus.Checking, JobStatus.AwaitingReview, JobStatus.Approved),
            Fact("No event is published after the event bus stopped", "late-events", text => text == "0"),
        ],
    };

    private static HashSet<JobId> Children(DogfoodEvidence evidence, JobId parent) =>
        [.. evidence.All<JobSubmitted>(submitted => submitted.Parent == Option<JobId>.Some(parent)).Select(submitted => submitted.Job)];
}
