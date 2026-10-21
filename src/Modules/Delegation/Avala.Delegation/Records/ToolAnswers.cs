using System.Globalization;
using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Delegation.Policy;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Records;

internal static class ToolAnswers
{
    public static ToolResult Refused(ItemId item, DelegationError error) => Refused(item, error, Reason(error));

    public static ToolResult Refused(ItemId item, DelegationError error, Option<ConnectionName> asked, IReadOnlyList<ConnectionName> declared) =>
        Refused(item, error, error == DelegationError.UndeclaredConnection ? Undeclared(asked, declared) : Reason(error));

    private static ToolResult Refused(ItemId item, DelegationError error, string reason) =>
        new(item, new JsonObject { ["refused"] = Camel(error), ["reason"] = reason }.ToJsonString()) { IsError = true };

    private static string Undeclared(Option<ConnectionName> asked, IReadOnlyList<ConnectionName> declared)
    {
        var named = asked.Match(connection => connection.Value, () => string.Empty);

        return declared.Count == 0
            ? $"the connection \"{named}\" is not declared in the repository's delegation section, which declares no connections to choose from; leave connection out."
            : $"the connection \"{named}\" is not declared in the repository's delegation section; choose one of the declared connections, {string.Join(", ", declared.Select(connection => connection.Value))}, or leave connection out.";
    }

    private const string CrossHarness = "crossHarness";

    public static ToolResult Reported(ItemId item, DelegationRecord delegation, ChildReport report)
    {
        var answer = new JsonObject
        {
            ["job"] = report.Child.Value.ToString(),
            ["outcome"] = Camel(report.Outcome),
            ["status"] = Camel(report.Status),
            ["state"] = Camel(Lifecycle.Of(report.Status, report.Hold, Asking.Nothing)),
            ["kind"] = CrossHarness,
            ["harness"] = report.Harness.Match(name => name, () => string.Empty),
            ["connection"] = delegation.Connection.Match(name => name.Value, () => string.Empty),
            ["autonomy"] = delegation.Autonomy.Match(level => Camel(level), () => string.Empty),
            ["role"] = Camel(delegation.Role),
            ["summary"] = report.Summary.Match(text => text, () => string.Empty),
            ["files"] = Files(report),
            ["verification"] = Verification(report),
            ["spent"] = Costs(report.Spent),
            ["tokens"] = report.Tokens,
            ["carve"] = Carve(report),
            ["integrated"] = Integrated(report),
            ["conflicts"] = new JsonArray([.. report.Conflicts.Select(path => Node(JsonValue.Create(path)))]),
            ["hold"] = report.Hold.Match(reason => Camel(reason), () => string.Empty),
            ["refusal"] = report.Refusal.Match(rejection => Camel(rejection), () => string.Empty),
            ["retries"] = report.Retries,
            ["reason"] = report.Outcome == ChildOutcome.GivenUp ? GaveUp(report) : report.Failure.Match(Failed, () => string.Empty),
        };

        return new ToolResult(item, answer.ToJsonString());
    }

    private static string Failed(JobFailure failure) => failure switch
    {
        JobFailure.WorkspaceUnavailable => "the child's worktree could not be prepared or checkpointed",
        JobFailure.ConnectionUnavailable => "no connection could run the child",
        JobFailure.ModelUnavailable => "the child's connection does not offer the model or effort it asked for",
        JobFailure.AgentFailed => "the child's agent ended its turn in failure",
        _ => "the child's agent could not take its message",
    };

    private static string GaveUp(ChildReport report) =>
        report.Retries == 0
            ? $"Avala could not continue it after {report.Hold.Match(Cause, () => "it stopped")}. Its job is discarded and none of its work reaches yours."
            : $"Avala gave it up after retrying it {report.Retries} time{(report.Retries == 1 ? string.Empty : "s")}, and then {report.Hold.Match(Cause, () => "it could not go on")} again. Its job is discarded and none of its work reaches yours.";

    public static string Retrying(JobHold hold) =>
        hold.Reason == HoldReason.SessionLost
            ? "Your session was lost before you finished. Continue the work where you left off."
            : $"Your last turn failed{hold.Problem.Match(problem => $": {problem}", () => string.Empty)}. Look at what went wrong, then continue the work where you left off.";

    public static string RetryNote(DelegationRecord delegation, JobId child, JobHold hold) =>
        $"Sub-agent \"{delegation.Instruction.Split('\n', 2)[0].Trim()}\", job {child.Value}, is retrying {delegation.Retries} of {ChildRetries.Most} after {Cause(hold.Reason)}: Avala continued it {(hold.Reason == HoldReason.SessionLost ? "in a new session" : "with its failure as feedback")}, and it keeps its work. Keep waiting for its report, or call stop_child with this child to stop it.";

    private static string Cause(HoldReason reason) => reason == HoldReason.SessionLost ? "its session was lost" : "its turn failed";

    public static ToolResult Running(ItemId item, DelegationRecord delegation)
    {
        var child = delegation.Child.Match(job => job.Value.ToString(), () => string.Empty);

        return new ToolResult(item, new JsonObject
        {
            ["job"] = child,
            ["outcome"] = "running",
            ["status"] = "running",
            ["state"] = Camel(SubagentState.Working),
            ["kind"] = CrossHarness,
            ["note"] = "this sub-agent is still working; its call returned early so that you can answer another sub-agent that asks you something. Call wait_child with this child to keep waiting for its report.",
            ["wait_child"] = new JsonObject { ["child"] = child },
        }.ToJsonString());
    }

    public static ToolResult Held(ItemId item, JobId child, JobHold hold) =>
        new(item, new JsonObject
        {
            ["job"] = child.Value.ToString(),
            ["outcome"] = Camel(ChildOutcome.Held),
            ["status"] = Camel(JobStatus.NeedsHelp),
            ["state"] = Camel(SubagentState.Waiting),
            ["kind"] = CrossHarness,
            ["hold"] = Camel(hold.Reason),
            ["reason"] = HeldBecause(hold),
            ["note"] = HeldNext,
            ["wait_child"] = new JsonObject { ["child"] = child.Value.ToString() },
            ["stop_child"] = new JsonObject { ["child"] = child.Value.ToString() },
        }.ToJsonString());

    public static string HeldNote(DelegationRecord delegation, JobId child, JobHold hold) =>
        $"Sub-agent \"{delegation.Instruction.Split('\n', 2)[0].Trim()}\", job {child.Value}, is held ({Camel(hold.Reason)}): {HeldBecause(hold)} {HeldNext}";

    private const string HeldNext = "Avala hands it to another connection your delegation section declares when one has room, or it waits for its window to reset; it keeps its work either way. Call wait_child with this child to keep waiting for its report, or stop_child to stop it.";

    private static string HeldBecause(JobHold hold) =>
        hold.Limit.Match(
            limit => $"its connection used {limit.UsedFraction.ToString("P0", CultureInfo.InvariantCulture)} of its {limit.Window} usage window.",
            () => "its connection reached its usage limit.");

    public static ToolResult Stopped(ItemId item, JobId child) =>
        new(item, new JsonObject
        {
            ["job"] = child.Value.ToString(),
            ["outcome"] = Camel(ChildOutcome.Discarded),
            ["status"] = Camel(JobStatus.Discarded),
            ["state"] = Camel(SubagentState.Stopped),
            ["kind"] = CrossHarness,
            ["note"] = "this sub-agent was stopped and its job discarded; none of its work reaches yours.",
        }.ToJsonString());

    public static ToolResult NotStopped(ItemId item) =>
        new(item, new JsonObject { ["refused"] = "notRunning", ["reason"] = "that sub-agent already ended, so there is nothing to stop." }.ToJsonString()) { IsError = true };

    public static ToolResult AlreadyTold(ItemId item, ChildReport report) =>
        new(item, new JsonObject
        {
            ["job"] = report.Child.Value.ToString(),
            ["outcome"] = Camel(report.Outcome),
            ["note"] = "this sub-agent's report already reached you; it has nothing more to wait for.",
        }.ToJsonString());

    public static string Briefing(IReadOnlyList<DelegationRecord> reported) =>
        string.Join(
            '\n',
            [
                "These delegate calls of yours were answered while you could not receive their results; each result is what its call returns. Do not delegate this work again.",
                .. reported.SelectMany(record => record.Report.Match<string[]>(
                    report => [$"delegate call {record.Item.Value}: {Reported(record.Item, record, report).Content}"],
                    () => [])),
            ]);

    private static JsonArray Files(ChildReport report) =>
        new([.. report.Files.Select(file => Node(new JsonObject
        {
            ["path"] = file.Path,
            ["change"] = Camel(file.Kind),
            ["added"] = file.Added.Match(lines => lines, () => 0),
            ["removed"] = file.Removed.Match(lines => lines, () => 0),
            ["binary"] = file.Added.IsNone,
        }))]);

    private static JsonNode Verification(ChildReport report) =>
        report.Verification.Match<JsonNode>(
            verified => new JsonObject
            {
                ["outcome"] = Camel(verified.Outcome),
                ["checks"] = new JsonArray([.. verified.Checks.Select(check => Node(new JsonObject
                {
                    ["name"] = check.Name,
                    ["status"] = Camel(check.Status),
                    ["exitCode"] = check.ExitCode.Match(code => code.ToString(CultureInfo.InvariantCulture), () => string.Empty),
                }))]),
            },
            () => new JsonObject { ["outcome"] = "none" });

    private static JsonNode Carve(ChildReport report) =>
        report.Carve.Match<JsonNode>(
            carve => new JsonObject { ["cost"] = Costs(carve.Cost), ["tokens"] = carve.Tokens.Match(tokens => tokens.ToString(CultureInfo.InvariantCulture), () => "uncapped") },
            () => new JsonObject());

    private static JsonNode Integrated(ChildReport report) =>
        report.Delivery.Match<JsonNode>(
            delivery => new JsonObject { ["branch"] = delivery.Branch, ["commit"] = delivery.Commit.Match(commit => commit, () => string.Empty) },
            () => new JsonObject());

    private static JsonArray Costs(IReadOnlyList<Cost> costs) =>
        new([.. costs.Select(cost => Node(new JsonObject { ["amount"] = cost.Amount, ["currency"] = cost.Currency }))]);

    private static JsonNode? Node(JsonNode node) => node;

    private static string Camel<TValue>(TValue value)
        where TValue : struct, Enum =>
        value.ToString() is var name ? $"{char.ToLowerInvariant(name[0])}{name[1..]}" : string.Empty;

    private static string Reason(DelegationError error) => error switch
    {
        DelegationError.MalformedInput => "the input needs an instruction as non-empty text of at most 4,000 characters, an autonomy of supervised or autonomous if any, a model, an effort and a connection as non-empty text if any, and a role of worker, reviewer or research if any.",
        DelegationError.UnofferedModel => "the sub-agent's connection does not offer that model; ask for one it offers or leave the model out.",
        DelegationError.UnofferedEffort => "the sub-agent's connection does not offer that effort level; ask for one it offers or leave the effort out.",
        DelegationError.NoJob => "this session runs no job, so there is nothing to delegate from.",
        DelegationError.NotDeclared => "the repository's .avala/jobs.json declares no delegation section, so this repository does not delegate.",
        DelegationError.DepthExceeded => "a sub-agent at this depth would be deeper than the repository's maxDepth allows; do the work yourself.",
        DelegationError.TooManyChildren => "you already have as many sub-agents running as the repository's maxChildren allows; wait for one to report back.",
        DelegationError.AutonomyLoosened => "a sub-agent may only run as strictly as you or stricter, and you are not autonomous.",
        DelegationError.NotSubmitted => "the harness could not submit the sub-agent's job.",
        DelegationError.RoleLoosened => "you, or this repository's delegation section, only allow read-only sub-agents: ask for role reviewer or research.",
        _ => "the repository's delegation section of .avala/jobs.json cannot be read from the job's base commit or is invalid.",
    };
}
