using System.Text;
using System.Text.Json;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Launching;
using Avala.Sdk;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.Logging;

namespace Avala.Jobs.JobFiles;

internal sealed partial class JobFileReader(IBaseFiles files, ILogger<JobFileReader> logger) : IRepositoryDefaults
{
    public const string JobFile = ".avala/jobs.json";

    public const int MaximumBytes = 16 * 1024;

    public const string ByCapacity = "auto";

    private const string ConnectionField = "connection";

    private const string ApprovalField = "approval";

    private const string AutopilotField = "autopilot";

    private const string DelegationField = "delegation";

    private const string ModelField = "model";

    private const string EffortField = "effort";

    private static readonly Declaration Nothing = new(Option<string>.None, Option<string>.None, ModelChoice.Default);

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 3, AllowDuplicateProperties = false };

    public async ValueTask<Result<Option<ConnectionName>, JobRejection>> ConnectionAsync(string worktree, CancellationToken cancellationToken) =>
        (await DeclaredAsync(worktree, cancellationToken))
            .Map(Preferred)
            .MapError(_ => JobRejection.UnusableConnection);

    public async ValueTask<Result<Option<ConnectionName>, JobRejection>> CurrentConnectionAsync(string repository, CancellationToken cancellationToken) =>
        (await files.ReadCurrentAsync(repository, JobFile, cancellationToken)).Match(
            file => file.Content.Match(Parse, () => Nothing).Map(Preferred),
            _ => Option<ConnectionName>.None);

    public async ValueTask<Result<ModelChoice, JobRejection>> ModelAsync(string worktree, CancellationToken cancellationToken) =>
        (await DeclaredAsync(worktree, cancellationToken)).Map(declared => declared.Model);

    public async ValueTask<ModelChoice> CurrentModelAsync(string repository, CancellationToken cancellationToken) =>
        (await files.ReadCurrentAsync(repository, JobFile, cancellationToken)).Match(
            file => file.Content.Match(Parse, () => Nothing).Match(declared => declared.Model, _ => ModelChoice.Default),
            _ => ModelChoice.Default);

    public async ValueTask<Result<Option<string>, JobRejection>> ApprovalAsync(string worktree, CancellationToken cancellationToken) =>
        (await DeclaredAsync(worktree, cancellationToken)).Map(declared => declared.Approval);

    private async Task<Result<Declaration, JobRejection>> DeclaredAsync(string worktree, CancellationToken cancellationToken) =>
        (await files.ReadAsync(worktree, JobFile, cancellationToken)).Match(
            file => file.Content.Match(Parse, () => Nothing),
            failure => Rejected($"it cannot be read from the base commit: {failure}"));

    private Result<Declaration, JobRejection> Parse(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaximumBytes)
        {
            return Rejected($"it is over {MaximumBytes} bytes");
        }

        try
        {
            using var document = JsonDocument.Parse(text, Options);

            return Declared(document.RootElement);
        }
        catch (JsonException)
        {
            return Rejected("it is not valid JSON");
        }
    }

    private Result<Declaration, JobRejection> Declared(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Rejected("it is not a JSON object");
        }

        if (root.EnumerateObject().Any(property => property.Name is not (ConnectionField or ApprovalField or AutopilotField or DelegationField or ModelField or EffortField)))
        {
            return Rejected("it has a field the format does not define");
        }

        if (root.TryGetProperty(AutopilotField, out var autopilot) && !IsSection(autopilot, listsAllowed: false))
        {
            return Rejected($"its {AutopilotField} is not an object of plain values");
        }

        if (root.TryGetProperty(DelegationField, out var delegation) && !IsSection(delegation, listsAllowed: true))
        {
            return Rejected($"its {DelegationField} is not an object of plain values and lists of them");
        }

        return Named(root, ConnectionField).Bind(connection => Named(root, ApprovalField).Bind(approval => Named(root, ModelField).Bind(model => Named(root, EffortField)
            .Map(effort => new Declaration(connection, approval, new ModelChoice(model, effort))))));
    }

    private static Option<ConnectionName> Preferred(Declaration declared) =>
        declared.Connection.Bind(name => name == ByCapacity ? Option<ConnectionName>.None : new ConnectionName(name));

    private static bool IsSection(JsonElement section, bool listsAllowed) =>
        section.ValueKind == JsonValueKind.Object
        && section.EnumerateObject().All(field => field.Value.ValueKind switch
        {
            JsonValueKind.Object => false,
            JsonValueKind.Array => listsAllowed && field.Value.EnumerateArray().All(item => item.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)),
            _ => true,
        });

    private Result<Option<string>, JobRejection> Named(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var named))
        {
            return Option<string>.None;
        }

        return named.ValueKind == JsonValueKind.String && named.GetString() is { Length: > 0 } name
            ? Option<string>.Some(name)
            : Rejected($"its {field} is not a name");
    }

    private JobRejection Rejected(string reason)
    {
        LogRejected(JobFile, reason);

        return JobRejection.InvalidJobFile;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The repository's {File} is rejected because {Reason}")]
    private partial void LogRejected(string file, string reason);

    private sealed record Declaration(Option<string> Connection, Option<string> Approval, ModelChoice Model);
}
