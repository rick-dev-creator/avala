using System.Globalization;
using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;

namespace Avala.Delegation.Records;

internal static class ToolAnswers
{
    public static ToolResult Refused(ItemId item, DelegationError error) =>
        new(item, new JsonObject { ["refused"] = Camel(error), ["reason"] = Reason(error) }.ToJsonString()) { IsError = true };

    public static ToolResult Reported(ItemId item, DelegationRecord delegation, ChildReport report)
    {
        var answer = new JsonObject
        {
            ["job"] = report.Child.Value.ToString(),
            ["outcome"] = Camel(report.Outcome),
            ["status"] = Camel(report.Status),
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
        };

        return new ToolResult(item, answer.ToJsonString());
    }

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
        DelegationError.MalformedInput => "the input needs an instruction as non-empty text of at most 4,000 characters, an autonomy of supervised or autonomous if any, a model and an effort as non-empty text if any, and a role of worker, reviewer or research if any.",
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
