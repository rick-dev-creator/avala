using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Permissions.Answering;
using Avala.Permissions.Contracts;
using Avala.Permissions.Policies;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Permissions.PolicyFiles;

internal sealed class RepositoryRuleWriter(IJobCatalog jobs, IWorkingFiles files) : IRepositoryRuleFiles
{
    private static readonly JsonSerializerOptions Written = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<Result<PolicyRule, PolicyError>> AddAsync(JobId job, PolicyRule rule, CancellationToken cancellationToken)
    {
        var repository = (await jobs.HistoryAsync(job, cancellationToken)).Match(history => history.Summary.Repository, () => string.Empty);

        if (repository.Length == 0 || !(await files.ReadAsync(repository, PermissionPolicy.PolicyFile, cancellationToken)).TryGetValue(out var current, out _))
        {
            return PolicyError.RepositoryUnwritable;
        }

        if (!Valid(current.Match(text => text, () => "{}")).Map(content => Added(content, rule)).Bind(Valid).TryGetValue(out var added, out var error))
        {
            return error;
        }

        return (await files.WriteAsync(repository, PermissionPolicy.PolicyFile, added, cancellationToken)).Match(
            _ => Result<PolicyRule, PolicyError>.Success(rule),
            _ => PolicyError.RepositoryUnwritable);
    }

    private static Result<string, PolicyError> Valid(string content) =>
        PolicyFileFormat.Problem(content).Match<Result<string, PolicyError>>(problem => problem, () => content);

    private static string Added(string content, PolicyRule rule)
    {
        var policy = JsonNode.Parse(content)?.AsObject() ?? [];
        if (policy["rules"] is not JsonArray rules)
        {
            rules = [];
            policy["rules"] = rules;
        }

        var declared = new JsonObject { ["name"] = rule.Name };

        foreach (var kind in rule.Kind.Match<ItemKind[]>(kind => [kind], () => []))
        {
            declared["kind"] = Named(kind);
        }

        foreach (var target in rule.Target.Match<string[]>(target => [target], () => []))
        {
            declared["target"] = target;
        }

        declared["answer"] = Named(rule.Answer);
        rules.Add(declared);

        return policy.ToJsonString(Written) + Environment.NewLine;
    }

    private static string Named<T>(T value)
        where T : struct, Enum =>
        JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
