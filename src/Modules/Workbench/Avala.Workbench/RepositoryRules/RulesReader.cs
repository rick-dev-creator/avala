using System.Text.Json;
using Avala.Budgets.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.RepositoryRules;

internal enum JobFileStatus
{
    Absent,
    Declared,
    Malformed,
    Unreadable,
}

internal sealed record JobFileSection(string Name, string Value);

internal sealed record JobFileState(JobFileStatus File, IReadOnlyList<JobFileSection> Sections, Option<FileOrigin> Origin);

internal sealed record RulesOfRepository(string Repository, RepositoryPolicy Policy, RepositoryBudget Budget, RepositoryChecks Checks, JobFileState Jobs);

internal sealed class RulesReader(IRepositoryPolicies policies, IRepositoryBudgets budgets, IRepositoryChecks checks, IBaseFiles files)
{
    public async ValueTask<RulesOfRepository> ReadAsync(string repository, CancellationToken cancellationToken) =>
        new(
            repository,
            await policies.OfRepositoryAsync(repository, cancellationToken),
            await budgets.OfRepositoryAsync(repository, cancellationToken),
            await checks.OfRepositoryAsync(repository, cancellationToken),
            (await files.ReadCurrentAsync(repository, RuleFiles.Jobs, cancellationToken)).Match(
                file => file.Content.Match(
                    text => Sections(text, file.Origin),
                    () => new JobFileState(JobFileStatus.Absent, [], file.Origin)),
                _ => new JobFileState(JobFileStatus.Unreadable, [], Option<FileOrigin>.None)));

    private static JobFileState Sections(string text, FileOrigin origin)
    {
        try
        {
            using var document = JsonDocument.Parse(text);

            return document.RootElement.ValueKind == JsonValueKind.Object
                ? new JobFileState(
                    JobFileStatus.Declared,
                    [.. document.RootElement.EnumerateObject().Select(section => new JobFileSection(section.Name, Value(section.Value)))],
                    origin)
                : new JobFileState(JobFileStatus.Malformed, [], origin);
        }
        catch (JsonException)
        {
            return new JobFileState(JobFileStatus.Malformed, [], origin);
        }
    }

    private static string Value(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
}

internal static class RuleFiles
{
    public const string Permissions = ".avala/permissions.json";

    public const string Budget = ".avala/budget.json";

    public const string Checks = ".avala/checks.json";

    public const string Jobs = ".avala/jobs.json";
}
